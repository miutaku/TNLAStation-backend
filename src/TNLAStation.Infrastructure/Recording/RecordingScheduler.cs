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
    IThumbnailService thumbnails,
    IRecordedHistoryStore history,
    ICommandHookRunner hooks,
    IClientNotifier notifier,
    RecordingRecovery recovery,
    IOptions<RecordingOptions> recordingOptions,
    IOptions<StorageOptions> storageOptions,
    IOptions<MirakurunOptions> mirakurunOptions,
    IOptions<CommandHookOptions> commandHookOptions,
    IRecordingLeaseProvider leaseProvider,
    IRecordingJobRegistry jobs,
    RecordingScheduleSignal scheduleSignal,
    TimeProvider timeProvider,
    ILogger<RecordingScheduler> logger) : BackgroundService
{
    private readonly RecordingOptions options = recordingOptions.Value;
    private readonly StorageOptions storage = storageOptions.Value;
    private readonly MirakurunOptions mirakurunConfig = mirakurunOptions.Value;
    private readonly CommandHookOptions hookOptions = commandHookOptions.Value;
    private readonly Dictionary<string, RecordingSession> running = new(StringComparer.Ordinal);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        TimeSpan interval = TimeSpan.FromSeconds(Math.Max(1, options.PollIntervalSeconds));

        while (!stoppingToken.IsCancellationRequested)
        {
            // 録るのは 1 つの実体だけ。複数が同じ予約を録ると、チューナーを二重に取り合う。
            IAsyncDisposable? lease = null;
            try
            {
                lease = await leaseProvider.TryAcquireAsync(stoppingToken);
                if (lease is not null)
                {
                    await recovery.RecoverAsync(stoppingToken);
                    await RunLoopAsync(interval, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                // DB が落ちていて鍵が取れない、復旧が読めない、といったときもここで受ける。
                // 落とすと録画そのものが二度と始まらない。次の周期でやり直す。
                LogSchedulerFailed(logger, exception);
            }
            finally
            {
                if (lease is not null)
                {
                    await lease.DisposeAsync();
                }
            }

            try
            {
                await Task.Delay(interval, timeProvider, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
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

            await scheduleSignal.WaitAsync(interval, timeProvider, stoppingToken);
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

            // マージンは時刻指定予約にだけ付ける。番組表予約は番組の時刻をそのまま使う —
            // EPGStation の timeSpecifiedStartMargin/EndMargin と同じ考え方。
            int startMargin = reserve.IsTimeSpecified ? options.StartMarginSeconds : 0;
            int endMargin = reserve.IsTimeSpecified ? options.EndMarginSeconds : 0;
            bool inWindow = now >= startAt.AddSeconds(-startMargin) &&
                now < endAt.AddSeconds(endMargin);
            string reserveKey = GetReserveKey(reserve);
            if (inWindow && !running.ContainsKey(reserveKey))
            {
                await StartAsync(reserve, reserveKey, startAt, endAt, cancellationToken);
            }
        }

        await StopFinishedAsync(now, page.Items);
    }

    private async Task StartAsync(
        Reservation reserve,
        string reserveKey,
        DateTimeOffset startAt,
        DateTimeOffset endAt,
        CancellationToken cancellationToken)
    {
        if (reserve.ProgramId is { } programId && await store.ExistsAsync(programId, cancellationToken))
        {
            // 既に録ったもの。予約が残っていても録り直さない。
            return;
        }

        EpgChannel? channel = await epg.GetChannelAsync(reserve.ChannelId, cancellationToken);
        ReserveHookPayload reservePayload = new(
            reserve.Id,
            reserve.ProgramId,
            reserve.ChannelId,
            channel?.Name ?? "CH",
            channel?.HalfWidthName ?? "CH",
            startAt.ToUnixTimeMilliseconds(),
            endAt.ToUnixTimeMilliseconds(),
            reserve.Name,
            reserve.HalfWidthName,
            reserve.Description,
            reserve.HalfWidthDescription,
            reserve.Extended,
            reserve.HalfWidthExtended,
            channel?.ChannelType);

        string? directory = ResolveDirectory();
        if (directory is null)
        {
            LogNoDirectory(logger);
            hooks.RunReserveHook(hookOptions.RecordingPrepRecFailedCommand, reservePayload);
            return;
        }

        Directory.CreateDirectory(directory);
        string template = reserve.RecordedFormat ?? options.RecordedFormat;
        string filename = RecordingFileName.EnsureUnique(
            directory,
            RecordingFileName.Create(template, options.RecordedFileExtension, startAt, channel, reserve));

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
                reserve.SubGenre3,
                reserve.Id,
                reserve.ReserveKey,
                reserve.ManualReserveId),
            directory,
            filename,
            cancellationToken);

        string finalPath = Path.Combine(directory, filename);
        string writePath = finalPath;
        if (!string.IsNullOrWhiteSpace(options.TempDirectory))
        {
            Directory.CreateDirectory(options.TempDirectory);
            writePath = Path.Combine(options.TempDirectory, filename);
        }

        var identity = new RecordingJobIdentity(
            recordedId,
            reserve.Id,
            reserveKey,
            reserve.ManualReserveId);
        int priority = reserve.IsConflict ? mirakurunConfig.ConflictPriority : mirakurunConfig.RecPriority;
        var session = new RecordingSession(
            reserve.Id,
            recordedId,
            videoFileId,
            reserve.ChannelId,
            reserve.RuleId,
            reserve.ProgramId,
            reserve.Name,
            reserve.HalfWidthName,
            reserve.Description,
            reserve.HalfWidthDescription,
            reserve.Extended,
            reserve.HalfWidthExtended,
            reservePayload.ChannelName,
            reservePayload.HalfWidthChannelName,
            reservePayload.ChannelType,
            startAt,
            endAt,
            finalPath,
            writePath,
            options.DropLogDirectory,
            priority,
            options.IsEnabledDropCheck,
            hookOptions.RecordingStartCommand,
            hookOptions.RecordingFinishCommand,
            hookOptions.RecordingFailedCommand,
            mirakurun,
            store,
            thumbnails,
            history,
            hooks,
            notifier,
            jobs,
            identity,
            logger);
        running[reserveKey] = session;
        hooks.RunReserveHook(hookOptions.RecordingPreStartCommand, reservePayload);
        session.Start();

        // BeginAsync とセッション登録の隙間に停止要求が来ても、取り消された予約を録り始めない。
        // Start は cancellation 済みでも空の録画行を AbortAsync するために一度呼んでいる。
        Reservation? current = await reserves.GetAsync(reserve.Id, cancellationToken);
        if (current is null || current.IsSkip)
        {
            running.Remove(reserveKey);
            await session.StopAsync();
            return;
        }

        LogRecordingStarted(logger, reserve.Name, filename);
    }

    private async Task StopFinishedAsync(DateTimeOffset now, IReadOnlyList<Reservation> current)
    {
        var reservesByKey = current.ToDictionary(GetReserveKey, StringComparer.Ordinal);
        foreach (string reserveKey in running.Keys.ToArray())
        {
            RecordingSession session = running[reserveKey];
            bool found = reservesByKey.TryGetValue(reserveKey, out Reservation? reserve);
            if (found && session.IsRunning)
            {
                if (session.ChannelId != reserve!.ChannelId)
                {
                    session.SwitchChannel(reserve.ChannelId);
                }

                await session.UpdateEndAtAsync(
                    DateTimeOffset.FromUnixTimeMilliseconds(reserve.EndAt),
                    CancellationToken.None);
            }

            bool overdue = !found ||
                now >= DateTimeOffset.FromUnixTimeMilliseconds(reserve!.EndAt)
                    .AddSeconds(reserve.IsTimeSpecified ? options.EndMarginSeconds : 0);
            if (!overdue && session.IsRunning)
            {
                continue;
            }

            running.Remove(reserveKey);
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

    private string? ResolveDirectory() =>
        options.Directory ?? (storage.RecordedDirectories.Count > 0 ? storage.RecordedDirectories[0].Path : null);

    private static string GetReserveKey(Reservation reserve) =>
        reserve.ReserveKey ?? $"reserve:{reserve.Id}";

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
        EventId = 4003,
        Level = LogLevel.Error,
        Message = "Could not check the reserves for recordings to start")]
    private static partial void LogTickFailed(ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = 4004,
        Level = LogLevel.Error,
        Message = "Could not acquire the recording lease or recover; retrying next cycle")]
    private static partial void LogSchedulerFailed(ILogger logger, Exception exception);
}
