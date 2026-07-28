using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TNLAStation.Application.Abstractions;
using TNLAStation.Domain;
using TNLAStation.Infrastructure.Configuration;
using TNLAStation.Infrastructure.Persistence;

namespace TNLAStation.Infrastructure.Transcoding;

/// <summary>
/// 待ち行列から取り出して変換する。実際に ffmpeg を回すのは
/// <see cref="IEncodeExecutor"/> の向こう側 (ffmpeg-worker)。ここは待ち行列の出し入れと
/// DB への確定だけを持つ。
///
/// 同時に走らせる本数は <see cref="EncodeOptions.ConcurrentEncodeNum"/> で決める
/// (EPGStation の concurrentEncodeNum 相当)。既定は 1 本 — エンコードは CPU を使い切るので、
/// 並べすぎると全部が遅くなり、録画そのものにも影響が出る。
/// </summary>
public sealed partial class EncodeWorker(
    IDbContextFactory<EpgDbContext> contextFactory,
    IVideoFileRepository videoFiles,
    IRecordedItemRepository recordedItems,
    IEpgRepository epg,
    IDropLogRepository dropLogs,
    IEncodeExecutor executor,
    ICommandHookRunner hooks,
    IOptions<EncodeOptions> encodeOptions,
    IOptions<CommandHookOptions> commandHookOptions,
    IRecordingLeaseProvider leaseProvider,
    IEncodeJobRegistry jobRegistry,
    IClientNotifier notifier,
    TimeProvider timeProvider,
    ILogger<EncodeWorker> logger) : BackgroundService
{
    private readonly CommandHookOptions hookOptions = commandHookOptions.Value;

    /// <summary>
    /// 設定が無いときの変換。放送の MPEG-2 を H.264 にして、どの機器でも再生できる形にする。
    /// </summary>
    private static readonly EncodeModeOptions[] DefaultModes =
    [
        new()
        {
            Name = "H.264",
            Extension = ".mp4",
            Arguments =
            [
                "-c:v", "libx264",
                "-preset", "veryfast",
                "-profile:v", "high",
                "-crf", "23",
                "-vf", "yadif",
                "-c:a", "aac",
                "-ar", "48000",
                "-b:a", "192k",
                "-ac", "2",
                "-movflags", "+faststart",
            ],
        },
    ];

    private readonly EncodeOptions options = encodeOptions.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        TimeSpan interval = TimeSpan.FromSeconds(Math.Max(1, options.PollIntervalSeconds));

        // 前回の実行中を待ちへ戻すのは 1 度きり。ただし起動時に DB が落ちていると失敗するので、
        // 成功するまで試みる。ここで落ちて諦めると、二度とエンコードが動かなくなる。
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ResetRunningTasksAsync(stoppingToken);
                break;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                LogEncodeFailed(logger, exception);
                await Task.Delay(interval, timeProvider, stoppingToken);
            }
        }

        // 「waiting」の取り出しは RunNextAsync 内の条件付き UPDATE で線形化されるので、
        // このループを複数本並べても同じ行を二重に掴むことはない。
        int concurrency = Math.Max(1, options.ConcurrentEncodeNum);
        await Task.WhenAll(Enumerable.Range(0, concurrency).Select(_ => PollLoopAsync(interval, stoppingToken)));
    }

    private async Task PollLoopAsync(TimeSpan interval, CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!await RunNextAsync(stoppingToken))
                {
                    await Task.Delay(interval, timeProvider, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                LogEncodeFailed(logger, exception);
                await Task.Delay(interval, timeProvider, stoppingToken);
            }
        }
    }

    /// <summary>
    /// 前回落ちたときに実行中だった行を待ちへ戻す。実行中のまま残すと、二度と取り出されない。
    /// </summary>
    private async Task ResetRunningTasksAsync(CancellationToken cancellationToken)
    {
        await using EpgDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await context.EncodeTasks
            .Where(task => task.Status == "running")
            .ExecuteUpdateAsync(
                task => task.SetProperty(x => x.Status, "waiting").SetProperty(x => x.Percent, (int?)null).SetProperty(x => x.Log, (string?)null),
                cancellationToken);
    }

    private async Task<bool> RunNextAsync(CancellationToken stoppingToken)
    {
        EncodeTaskEntity? task;
        await using (EpgDbContext selectContext = await contextFactory.CreateDbContextAsync(stoppingToken))
        {
            task = await selectContext.EncodeTasks.AsNoTracking()
                .Where(item => item.Status == "waiting")
                .OrderBy(item => item.Id)
                .FirstOrDefaultAsync(stoppingToken);
        }

        if (task is null)
        {
            return false;
        }

        // 選んだ直後から、ffprobe・ffmpeg・DB への完了登録までを 1 つのジョブとして
        // 公開する。DELETE がこの登録より先なら、下の条件付き UPDATE が 0 件になり
        // 実行しない。登録より後なら jobToken が止まる。
        using var jobCancellation = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        using IDisposable registration = jobRegistry.Register(task.Id, jobCancellation);
        CancellationToken jobToken = jobCancellation.Token;

        try
        {
            await using (EpgDbContext claimContext = await contextFactory.CreateDbContextAsync(jobToken))
            {
                int claimed = await claimContext.EncodeTasks
                    .Where(item => item.Id == task.Id && item.Status == "waiting")
                    .ExecuteUpdateAsync(
                        update => update.SetProperty(item => item.Status, "running"),
                        jobToken);
                if (claimed != 1)
                {
                    return true;
                }
            }

            await using IAsyncDisposable? lease = await leaseProvider.TryAcquireAsync(jobToken);
            await EncodeAsync(task, jobToken);
        }
        catch (OperationCanceledException) when (
            jobToken.IsCancellationRequested &&
            !stoppingToken.IsCancellationRequested)
        {
            // 利用者による取り消し。待ち行列側も削除するが、競合して先にこちらへ
            // 届く場合があるため冪等に掃除する。
            await RemoveTaskAsync(task.Id, CancellationToken.None);
        }

        return true;
    }

    private async Task EncodeAsync(EncodeTaskEntity task, CancellationToken cancellationToken)
    {
        VideoFileLocation? source = await videoFiles.GetAsync(task.SourceVideoFileId, cancellationToken);
        if (source is null || !File.Exists(source.FullPath))
        {
            await RemoveTaskAsync(task.Id, cancellationToken);
            return;
        }

        EncodeModeOptions mode = ResolveMode(task.Mode);
        string directory = task.ParentDirectoryName ?? source.ParentDirectoryName;
        if (!string.IsNullOrWhiteSpace(task.Directory))
        {
            directory = Path.Combine(directory, task.Directory);
        }

        Directory.CreateDirectory(directory);
        string filename = $"{Path.GetFileNameWithoutExtension(source.Filename)}-{mode.Name}{mode.Extension}";
        string output = Path.Combine(directory, filename);
        string partial = Path.Combine(
            directory,
            $"{Path.GetFileNameWithoutExtension(source.Filename)}-{mode.Name}.part{mode.Extension}");

        RecordedProgram? recordedInfo = await recordedItems.GetAsync(task.RecordedId, cancellationToken);
        EpgChannel? channel = recordedInfo is null
            ? null
            : await epg.GetChannelAsync(recordedInfo.ChannelId, cancellationToken);
        DropLogFileLocation? dropLog = recordedInfo?.DropLogFile is { } dropLogFile
            ? await dropLogs.GetAsync(dropLogFile.Id, cancellationToken)
            : null;
        IReadOnlyDictionary<string, string> environmentVariables = BuildEncodeEnvironment(
            task, source, output, recordedInfo, channel, dropLog);

        bool succeeded;
        try
        {
            succeeded = await executor.RunAsync(
                source.FullPath,
                partial,
                mode.Arguments,
                mode.Command,
                mode.RateTimeoutMultiplier,
                environmentVariables,
                onProgress: (percent, log, ct) => PersistProgressAsync(task.Id, percent, log, ct),
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
        }
        catch
        {
            TryDelete(partial);
            throw;
        }

        if (!succeeded)
        {
            // 中途半端なファイルを残すと、再生できないものが一覧に並ぶ。
            TryDelete(partial);
            await RemoveTaskAsync(task.Id, cancellationToken);
            return;
        }

        try
        {
            File.Move(partial, output, overwrite: true);
        }
        catch
        {
            TryDelete(partial);
            throw;
        }

        long? videoFileId;
        try
        {
            videoFileId = await TryCommitAsync(task, directory, filename, mode, cancellationToken);
        }
        catch
        {
            TryDelete(output);
            throw;
        }

        if (videoFileId is null)
        {
            // キャンセル側が先にタスク行を消した。完成ファイルだけを残してはいけない。
            TryDelete(output);
            return;
        }

        if (task.RemoveOriginal)
        {
            await videoFiles.DeleteAsync(source.Id, cancellationToken);
        }

        var file = new FileInfo(output);
        LogEncodeFinished(logger, filename, file.Exists ? file.Length : 0);

        long channelId = recordedInfo?.ChannelId ?? 0;
        // 上流は EncodeFinishModel が notifyClient と notifyUpdateEncodeProgress の両方を鳴らす。
        notifier.NotifyClient();
        notifier.NotifyUpdateEncodeProgress();
        hooks.RunEncodeFinishHook(hookOptions.EncodingFinishCommand, new EncodeFinishHookPayload(
            task.RecordedId,
            videoFileId,
            output,
            mode.Name,
            channelId,
            channel?.Name ?? channelId.ToString(CultureInfo.InvariantCulture),
            channel?.HalfWidthName ?? channelId.ToString(CultureInfo.InvariantCulture),
            recordedInfo?.Name ?? filename,
            recordedInfo?.HalfWidthName ?? filename,
            recordedInfo?.Description,
            recordedInfo?.HalfWidthDescription,
            recordedInfo?.Extended,
            recordedInfo?.HalfWidthExtended));
    }

    /// <summary>
    /// EPGStation の encode.cmd と同じ変数一式を環境変数として渡す。TNLAStation では固定引数
    /// (<see cref="EncodeModeOptions.Arguments"/>) の実行でも無害なので、常に渡す。
    /// </summary>
    private static Dictionary<string, string> BuildEncodeEnvironment(
        EncodeTaskEntity task,
        VideoFileLocation source,
        string outputPath,
        RecordedProgram? recordedInfo,
        EpgChannel? channel,
        DropLogFileLocation? dropLog)
    {
        long channelId = recordedInfo?.ChannelId ?? 0;
        return new Dictionary<string, string>
        {
            ["RECORDEDID"] = task.RecordedId.ToString(CultureInfo.InvariantCulture),
            // EPGStation は拡張子 (suffix) 指定時、DIR に出力ファイルのフルパスを入れる
            // (ディレクトリではない) — ここではその実挙動に合わせる。
            ["DIR"] = outputPath,
            ["SUBDIR"] = task.Directory ?? string.Empty,
            ["NAME"] = recordedInfo?.Name ?? source.Name,
            ["HALF_WIDTH_NAME"] = recordedInfo?.HalfWidthName ?? source.Name,
            ["DESCRIPTION"] = recordedInfo?.Description ?? string.Empty,
            ["HALF_WIDTH_DESCRIPTION"] = recordedInfo?.HalfWidthDescription ?? string.Empty,
            ["EXTENDED"] = recordedInfo?.Extended ?? string.Empty,
            ["HALF_WIDTH_EXTENDED"] = recordedInfo?.HalfWidthExtended ?? string.Empty,
            ["VIDEOTYPE"] = recordedInfo?.VideoType ?? string.Empty,
            ["VIDEORESOLUTION"] = recordedInfo?.VideoResolution ?? string.Empty,
            ["VIDEOSTREAMCONTENT"] = recordedInfo?.VideoStreamContent?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            ["VIDEOCOMPONENTTYPE"] = recordedInfo?.VideoComponentType?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            ["AUDIOSAMPLINGRATE"] = recordedInfo?.AudioSamplingRate?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            ["AUDIOCOMPONENTTYPE"] = recordedInfo?.AudioComponentType?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            ["CHANNELID"] = channelId.ToString(CultureInfo.InvariantCulture),
            ["CHANNELNAME"] = channel?.Name ?? string.Empty,
            ["HALF_WIDTH_CHANNELNAME"] = channel?.HalfWidthName ?? string.Empty,
            ["GENRE1"] = recordedInfo?.Genre1?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            ["SUBGENRE1"] = recordedInfo?.SubGenre1?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            ["GENRE2"] = recordedInfo?.Genre2?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            ["SUBGENRE2"] = recordedInfo?.SubGenre2?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            ["GENRE3"] = recordedInfo?.Genre3?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            ["SUBGENRE3"] = recordedInfo?.SubGenre3?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            ["START_AT"] = recordedInfo?.StartAt.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            ["END_AT"] = recordedInfo?.EndAt.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            ["DROPLOG_ID"] = recordedInfo?.DropLogFile?.Id.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            ["DROPLOG_PATH"] = dropLog?.FullPath ?? string.Empty,
            ["ERROR_CNT"] = recordedInfo?.DropLogFile?.ErrorCount.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            ["DROP_CNT"] = recordedInfo?.DropLogFile?.DropCount.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            ["SCRAMBLING_CNT"] = recordedInfo?.DropLogFile?.ScramblingCount.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
        };
    }

    /// <summary>戻り値は確定した VideoFile の id。キャンセル側が先に行を消していれば null。</summary>
    private async Task<long?> TryCommitAsync(
        EncodeTaskEntity task,
        string directory,
        string filename,
        EncodeModeOptions mode,
        CancellationToken cancellationToken)
    {
        var file = new FileInfo(Path.Combine(directory, filename));
        await using EpgDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        // この DELETE がキャンセルとの線形化点。キャンセル側が先に消していれば、
        // VideoFile は追加しない。こちらが先なら、ファイル登録と同じ transaction で確定する。
        int claimed = await context.EncodeTasks
            .Where(item => item.Id == task.Id)
            .ExecuteDeleteAsync(cancellationToken);
        if (claimed != 1)
        {
            return null;
        }

        var videoFile = new VideoFileEntity
        {
            RecordedId = task.RecordedId,
            Name = mode.Name,
            Filename = Path.GetRelativePath(directory, file.FullName),
            ParentDirectoryName = directory,
            Type = "encoded",
            Size = file.Exists ? file.Length : 0,
            CreatedAt = timeProvider.GetUtcNow(),
        };
        context.VideoFiles.Add(videoFile);
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return videoFile.Id;
    }

    private async Task PersistProgressAsync(long taskId, int? percent, string? log, CancellationToken cancellationToken)
    {
        await using EpgDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await context.EncodeTasks
            .Where(task => task.Id == taskId)
            .ExecuteUpdateAsync(
                task => task.SetProperty(x => x.Percent, percent).SetProperty(x => x.Log, log),
                cancellationToken);
    }

    private async Task RemoveTaskAsync(long taskId, CancellationToken cancellationToken)
    {
        await using EpgDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await context.EncodeTasks.Where(task => task.Id == taskId).ExecuteDeleteAsync(cancellationToken);
    }

    private EncodeModeOptions ResolveMode(string name)
    {
        IReadOnlyList<EncodeModeOptions> modes = options.Modes.Count > 0 ? options.Modes : DefaultModes;
        return modes.FirstOrDefault(mode => string.Equals(mode.Name, name, StringComparison.Ordinal))
            ?? modes[0];
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    [LoggerMessage(
        EventId = 5000,
        Level = LogLevel.Information,
        Message = "Encoded {FileName} ({Size} bytes)")]
    private static partial void LogEncodeFinished(ILogger logger, string fileName, long size);

    [LoggerMessage(
        EventId = 5001,
        Level = LogLevel.Error,
        Message = "An encode task failed; the queue continues with the next one")]
    private static partial void LogEncodeFailed(ILogger logger, Exception exception);
}
