using System.Diagnostics;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TNLAStation.Application.Abstractions;
using TNLAStation.Infrastructure.Configuration;
using TNLAStation.Infrastructure.Persistence;

namespace TNLAStation.Infrastructure.Encoding;

/// <summary>
/// 待ち行列から 1 件ずつ取り出して変換する。
///
/// 同時に何本も走らせない。エンコードは CPU を使い切るので、並べても全部が遅くなるだけで、
/// 録画そのものにも影響が出る。
/// </summary>
public sealed partial class EncodeWorker(
    IDbContextFactory<EpgDbContext> contextFactory,
    IVideoFileRepository videoFiles,
    IMediaProbe probe,
    IOptions<EncodeOptions> encodeOptions,
    IOptions<StreamingOptions> streamingOptions,
    IRecordingLeaseProvider leaseProvider,
    TimeProvider timeProvider,
    ILogger<EncodeWorker> logger) : BackgroundService
{
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
    private readonly StreamingOptions streaming = streamingOptions.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        TimeSpan interval = TimeSpan.FromSeconds(Math.Max(1, options.PollIntervalSeconds));
        await ResetRunningTasksAsync(stoppingToken);

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
                task => task.SetProperty(x => x.Status, "waiting").SetProperty(x => x.Percent, (int?)null),
                cancellationToken);
    }

    private async Task<bool> RunNextAsync(CancellationToken cancellationToken)
    {
        EncodeTaskEntity? task;
        await using (EpgDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken))
        {
            task = await context.EncodeTasks
                .Where(item => item.Status == "waiting")
                .OrderBy(item => item.Id)
                .FirstOrDefaultAsync(cancellationToken);
            if (task is null)
            {
                return false;
            }

            task.Status = "running";
            await context.SaveChangesAsync(cancellationToken);
        }

        await using IAsyncDisposable? lease = await leaseProvider.TryAcquireAsync(cancellationToken);
        await EncodeAsync(task, cancellationToken);
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

        double? total = await probe.GetDurationSecondsAsync(source.FullPath, cancellationToken);
        bool succeeded = await RunFfmpegAsync(task.Id, source.FullPath, output, mode, total, cancellationToken);
        if (!succeeded)
        {
            // 中途半端なファイルを残すと、再生できないものが一覧に並ぶ。
            TryDelete(output);
            await RemoveTaskAsync(task.Id, cancellationToken);
            return;
        }

        await CompleteAsync(task, source, directory, filename, mode, cancellationToken);
    }

    private async Task<bool> RunFfmpegAsync(
        long taskId,
        string input,
        string output,
        EncodeModeOptions mode,
        double? totalSeconds,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo(streaming.FfmpegPath)
        {
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (string argument in CreateArguments(input, output, mode))
        {
            startInfo.ArgumentList.Add(argument);
        }

        using Process? process = Process.Start(startInfo);
        if (process is null)
        {
            return false;
        }

        // 進み具合は ffmpeg の -progress から拾う。行ごとに key=value で出てくる。
        while (await process.StandardError.ReadLineAsync(cancellationToken) is { } line)
        {
            if (totalSeconds is { } total and > 0 && TryReadOutTime(line, out double seconds))
            {
                int percent = (int)Math.Clamp(seconds / total * 100, 0, 100);
                await UpdatePercentAsync(taskId, percent, cancellationToken);
            }
        }

        await process.WaitForExitAsync(cancellationToken);
        return process.ExitCode == 0;
    }

    private static string[] CreateArguments(string input, string output, EncodeModeOptions mode)
    {
        var arguments = new List<string>
        {
            "-hide_banner",
            "-nostats",
            // 進み具合を読むために、経過を標準エラーへ機械が読める形で出させる。
            "-progress", "pipe:2",
            "-loglevel", "error",
            "-fflags", "+discardcorrupt",
            "-analyzeduration", "10M",
            "-probesize", "32M",
            "-dual_mono_mode", "main",
            "-i", input,
            "-map", "0:v:0",
            "-map", "0:a:0",
            "-ignore_unknown",
            "-sn",
            "-dn",
        };
        arguments.AddRange(mode.Arguments);
        arguments.Add("-y");
        arguments.Add(output);
        return [.. arguments];
    }

    private async Task CompleteAsync(
        EncodeTaskEntity task,
        VideoFileLocation source,
        string directory,
        string filename,
        EncodeModeOptions mode,
        CancellationToken cancellationToken)
    {
        var file = new FileInfo(Path.Combine(directory, filename));
        await using (EpgDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken))
        {
            context.VideoFiles.Add(new VideoFileEntity
            {
                RecordedId = task.RecordedId,
                Name = mode.Name,
                Filename = Path.GetRelativePath(directory, file.FullName),
                ParentDirectoryName = directory,
                Type = "encoded",
                Size = file.Exists ? file.Length : 0,
                CreatedAt = timeProvider.GetUtcNow(),
            });
            await context.EncodeTasks.Where(item => item.Id == task.Id).ExecuteDeleteAsync(cancellationToken);
            await context.SaveChangesAsync(cancellationToken);
        }

        if (task.RemoveOriginal)
        {
            await videoFiles.DeleteAsync(source.Id, cancellationToken);
        }

        LogEncodeFinished(logger, filename, file.Exists ? file.Length : 0);
    }

    private async Task UpdatePercentAsync(long taskId, int percent, CancellationToken cancellationToken)
    {
        await using EpgDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await context.EncodeTasks
            .Where(task => task.Id == taskId)
            .ExecuteUpdateAsync(task => task.SetProperty(x => x.Percent, percent), cancellationToken);
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

    private static bool TryReadOutTime(string line, out double seconds)
    {
        seconds = 0;
        const string key = "out_time_ms=";
        if (!line.StartsWith(key, StringComparison.Ordinal))
        {
            return false;
        }

        // 名前に反して単位はマイクロ秒。ffmpeg の出力がそうなっている。
        if (!long.TryParse(line[key.Length..], NumberStyles.Integer, CultureInfo.InvariantCulture, out long value))
        {
            return false;
        }

        seconds = value / 1_000_000d;
        return true;
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
            // 消せなくても次の変換は進められる。
        }
        catch (UnauthorizedAccessException)
        {
            // 同上。
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
