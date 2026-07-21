using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TNLAStation.Application.Abstractions;
using TNLAStation.Application.Models;
using TNLAStation.Domain;
using TNLAStation.Infrastructure.Configuration;
using TNLAStation.Infrastructure.Mirakurun;

namespace TNLAStation.Infrastructure.Recording;

/// <summary>
/// 予約の時刻が来たら録り始め、終わったら閉じる。
///
/// 予約を先に読んでタイマーを張るのではなく、短い間隔で予約表を見に行く。番組表は放送直前
/// まで動くので、張ったタイマーは古くなる。見に行くほうが、予約が変わっても追従できる。
/// </summary>
public sealed partial class RecordingScheduler(
    IReserveRepository reserves,
    IRecordingStore store,
    IEpgRepository epg,
    IMirakurunClient mirakurun,
    IOptions<RecordingOptions> recordingOptions,
    IOptions<StorageOptions> storageOptions,
    IRecordingLeaseProvider leaseProvider,
    TimeProvider timeProvider,
    ILogger<RecordingScheduler> logger) : BackgroundService
{
    private readonly RecordingOptions options = recordingOptions.Value;
    private readonly StorageOptions storage = storageOptions.Value;
    private readonly Dictionary<long, RecordingSession> running = [];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        TimeSpan interval = TimeSpan.FromSeconds(Math.Max(1, options.PollIntervalSeconds));

        while (!stoppingToken.IsCancellationRequested)
        {
            // 録るのは 1 つの実体だけ。複数が同じ予約を録ると、チューナーを二重に取り合う。
            await using IAsyncDisposable? lease = await leaseProvider.TryAcquireAsync(stoppingToken);
            if (lease is null)
            {
                await Task.Delay(interval, timeProvider, stoppingToken);
                continue;
            }

            await RecoverUnfinishedAsync(stoppingToken);
            await RunLoopAsync(interval, stoppingToken);
        }

        await StopAllAsync();
    }

    private async Task RunLoopAsync(TimeSpan interval, CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await TickAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                // 1 回の失敗で録画そのものを止めない。次の周期でまた見に行く。
                LogTickFailed(logger, exception);
            }

            await Task.Delay(interval, timeProvider, stoppingToken);
        }
    }

    private async Task TickAsync(CancellationToken cancellationToken)
    {
        DateTimeOffset now = timeProvider.GetUtcNow();
        Page<Reservation> page = await reserves.ListAsync(
            new ReserveQuery(IsHalfWidth: false, Offset: 0, Limit: int.MaxValue, Type: "normal"),
            cancellationToken);

        foreach (Reservation reserve in page.Items)
        {
            DateTimeOffset startAt = DateTimeOffset.FromUnixTimeMilliseconds(reserve.StartAt);
            DateTimeOffset endAt = DateTimeOffset.FromUnixTimeMilliseconds(reserve.EndAt);
            bool inWindow = now >= startAt.AddSeconds(-options.StartMarginSeconds) &&
                now < endAt.AddSeconds(options.EndMarginSeconds);
            if (inWindow && !running.ContainsKey(reserve.Id))
            {
                await StartAsync(reserve, startAt, endAt, cancellationToken);
            }
        }

        await StopFinishedAsync(now, page.Items);
    }

    private async Task StartAsync(
        Reservation reserve,
        DateTimeOffset startAt,
        DateTimeOffset endAt,
        CancellationToken cancellationToken)
    {
        if (reserve.ProgramId is { } programId && await store.ExistsAsync(programId, cancellationToken))
        {
            // 既に録ったもの。予約が残っていても録り直さない。
            return;
        }

        string? directory = ResolveDirectory();
        if (directory is null)
        {
            LogNoDirectory(logger);
            return;
        }

        EpgChannel? channel = await epg.GetChannelAsync(reserve.ChannelId, cancellationToken);
        Directory.CreateDirectory(directory);
        string filename = RecordingFileName.EnsureUnique(
            directory,
            RecordingFileName.Create(startAt, channel?.HalfWidthName ?? "CH", reserve.HalfWidthName));

        (long recordedId, long videoFileId) = await store.BeginAsync(
            new RecordingStart(
                reserve.ProgramId,
                reserve.RuleId,
                reserve.ChannelId,
                startAt,
                endAt,
                reserve.Name,
                reserve.HalfWidthName,
                reserve.Description,
                reserve.HalfWidthDescription,
                reserve.Extended,
                reserve.HalfWidthExtended,
                reserve.Genre1,
                reserve.SubGenre1,
                reserve.Genre2,
                reserve.SubGenre2,
                reserve.Genre3,
                reserve.SubGenre3),
            directory,
            filename,
            cancellationToken);

        var session = new RecordingSession(
            reserve.Id,
            recordedId,
            videoFileId,
            reserve.ChannelId,
            Path.Combine(directory, filename),
            mirakurun,
            store,
            logger);
        running[reserve.Id] = session;
        session.Start();
        LogRecordingStarted(logger, reserve.Name, filename);
    }

    private async Task StopFinishedAsync(DateTimeOffset now, IReadOnlyList<Reservation> current)
    {
        var reserveIds = current.ToDictionary(reserve => reserve.Id);
        foreach (long reserveId in running.Keys.ToArray())
        {
            RecordingSession session = running[reserveId];
            bool overdue = !reserveIds.TryGetValue(reserveId, out Reservation? reserve) ||
                now >= DateTimeOffset.FromUnixTimeMilliseconds(reserve.EndAt).AddSeconds(options.EndMarginSeconds);
            if (!overdue && session.IsRunning)
            {
                continue;
            }

            running.Remove(reserveId);
            await session.StopAsync();
        }
    }

    private async Task StopAllAsync()
    {
        foreach (RecordingSession session in running.Values)
        {
            await session.StopAsync();
        }

        running.Clear();
    }

    /// <summary>
    /// 前回落ちたときに書きかけだった録画を畳む。録画中のままにしておくと、いつまでも
    /// 「録画中」に出続け、同じ番組を録り直すこともできない。
    /// </summary>
    private async Task RecoverUnfinishedAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<UnfinishedRecording> unfinished = await store.ListUnfinishedAsync(cancellationToken);
        foreach (UnfinishedRecording item in unfinished)
        {
            var file = new FileInfo(Path.Combine(item.ParentDirectoryName, item.Filename));
            if (file.Exists && file.Length > 0)
            {
                await store.CompleteAsync(item.RecordedId, item.VideoFileId, file.Length, cancellationToken);
                LogRecoveredRecording(logger, item.Filename, file.Length);
            }
            else
            {
                // 1 バイトも書けていない。録画として残す意味がない。
                await store.AbortAsync(item.RecordedId, cancellationToken);
            }
        }
    }

    private string? ResolveDirectory() =>
        options.Directory ?? (storage.RecordedDirectories.Count > 0 ? storage.RecordedDirectories[0].Path : null);

    [LoggerMessage(
        EventId = 4000,
        Level = LogLevel.Information,
        Message = "Started recording {ProgramName} into {FileName}")]
    private static partial void LogRecordingStarted(ILogger logger, string programName, string fileName);

    [LoggerMessage(
        EventId = 4001,
        Level = LogLevel.Warning,
        Message = "No recording directory is configured; nothing can be recorded")]
    private static partial void LogNoDirectory(ILogger logger);

    [LoggerMessage(
        EventId = 4002,
        Level = LogLevel.Information,
        Message = "Closed the recording {FileName} left behind by a previous run ({Size} bytes)")]
    private static partial void LogRecoveredRecording(ILogger logger, string fileName, long size);

    [LoggerMessage(
        EventId = 4003,
        Level = LogLevel.Error,
        Message = "Could not check the reserves for recordings to start")]
    private static partial void LogTickFailed(ILogger logger, Exception exception);
}
