using System.Globalization;
using Microsoft.Extensions.Logging;
using TNLAStation.Application.Abstractions;
using TNLAStation.Domain;
using TNLAStation.Infrastructure.Mirakurun;

namespace TNLAStation.Infrastructure.Recording;

/// <summary>
/// 録画 1 本。チューナーから届く MPEG-TS を、そのままファイルへ書く。放送波をそのまま
/// 残すので変換はしない。変換は後からでもできるが、落とした情報は戻らない。
/// </summary>
internal sealed partial class RecordingSession(
    long reserveId,
    long recordedId,
    long videoFileId,
    long channelId,
    long? ruleId,
    long? programId,
    string name,
    string halfWidthName,
    string? description,
    string? halfWidthDescription,
    string? extended,
    string? halfWidthExtended,
    string channelName,
    string halfWidthChannelName,
    string? channelType,
    DateTimeOffset startAt,
    DateTimeOffset endAt,
    string path,
    string writePath,
    string? dropLogDirectory,
    int mirakurunPriority,
    bool isEnabledDropCheck,
    string? recordingStartCommand,
    string? recordingFinishCommand,
    string? recordingFailedCommand,
    IMirakurunClient mirakurun,
    IRecordingStore store,
    IThumbnailService thumbnails,
    IRecordedHistoryStore history,
    ICommandHookRunner hooks,
    IClientNotifier notifier,
    IRecordingJobRegistry jobs,
    RecordingJobIdentity identity,
    ILogger logger) : IDisposable
{
    private readonly CancellationTokenSource lifetime = new();
    private readonly object streamGate = new();
    private long currentChannelId = channelId;
    private long currentEndAt = endAt.ToUnixTimeMilliseconds();
    private CancellationTokenSource? activeStreamCancellation;
    private IDisposable? registration;
    private Task? worker;

    public long ReserveId => reserveId;

    public bool IsRunning => worker is { IsCompleted: false };

    public long ChannelId => Interlocked.Read(ref currentChannelId);

    public void SwitchChannel(long updatedChannelId)
    {
        if (Interlocked.Exchange(ref currentChannelId, updatedChannelId) == updatedChannelId)
        {
            return;
        }

        lock (streamGate)
        {
            activeStreamCancellation?.Cancel();
        }
    }

    public async ValueTask UpdateEndAtAsync(DateTimeOffset updatedEndAt, CancellationToken cancellationToken)
    {
        long value = updatedEndAt.ToUnixTimeMilliseconds();
        if (Interlocked.Read(ref currentEndAt) == value)
        {
            return;
        }

        await store.UpdateEndAtAsync(recordedId, updatedEndAt, cancellationToken);
        Interlocked.Exchange(ref currentEndAt, value);
    }

    public void Start()
    {
        if (worker is not null || registration is not null)
        {
            throw new InvalidOperationException($"Recording {recordedId} has already been started.");
        }

        registration = jobs.Register(identity, lifetime);
        worker = Task.Run(() => RunAsync(lifetime.Token), CancellationToken.None);
    }

    public async Task StopAsync()
    {
        await lifetime.CancelAsync();
        try
        {
            if (worker is not null)
            {
                try
                {
                    await worker;
                }
                catch (OperationCanceledException)
                {
                    // 終了の合図。
                }
            }
        }
        finally
        {
            Dispose();
        }
    }

    public void Dispose()
    {
        registration?.Dispose();
        lifetime.Dispose();
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            await RecordAsync(cancellationToken);
        }
        finally
        {
            // Completion はファイルと DB の確定後にだけ完了させる。HTTP の停止応答が先に
            // 返ると、直後の再生や一覧更新が書き込み途中の状態を見るため。
            registration?.Dispose();
        }
    }

    private async Task RecordAsync(CancellationToken cancellationToken)
    {
        long written = 0;
        var analyzer = new TransportStreamAnalyzer();
        try
        {
            hooks.RunRecordedHook(recordingStartCommand, BuildRecordedPayload());
            notifier.NotifyClient();
            Directory.CreateDirectory(Path.GetDirectoryName(writePath) is { Length: > 0 } writeDirectory ? writeDirectory : ".");
            await using var destination = new FileStream(
                writePath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.Read,
                bufferSize: 1 << 16,
                useAsync: true);

            byte[] buffer = new byte[1 << 16];
            while (!cancellationToken.IsCancellationRequested)
            {
                using var streamCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                lock (streamGate)
                {
                    activeStreamCancellation = streamCancellation;
                }

                long openedChannelId = ChannelId;
                try
                {
                    await using Stream source = await mirakurun.OpenServiceStreamAsync(
                        openedChannelId,
                        streamCancellation.Token,
                        mirakurunPriority);
                    while (true)
                    {
                        int read = await source.ReadAsync(buffer, streamCancellation.Token);
                        if (read == 0)
                        {
                            break;
                        }

                        if (isEnabledDropCheck)
                        {
                            analyzer.Append(buffer.AsSpan(0, read));
                        }

                        await destination.WriteAsync(
                            buffer.AsMemory(0, read),
                            streamCancellation.Token);
                        written += read;
                    }
                }
                catch (OperationCanceledException) when (
                    !cancellationToken.IsCancellationRequested && ChannelId != openedChannelId)
                {
                    // イベントリレー。ファイルは閉じず、次のサービスのストリームへ接続し直す。
                    continue;
                }
                finally
                {
                    lock (streamGate)
                    {
                        if (ReferenceEquals(activeStreamCancellation, streamCancellation))
                        {
                            activeStreamCancellation = null;
                        }
                    }
                }

                break;
            }
        }
        catch (OperationCanceledException)
        {
            // 予定の終了時刻。ここまで書けた分がそのまま録画になる。
        }
        catch (Exception exception)
        {
            // チューナーが落ちても、そこまで録れた分は残す。全部消すより役に立つ。
            LogRecordingFailed(logger, path, exception);
            hooks.RunRecordedHook(recordingFailedCommand, BuildRecordedPayload());
            notifier.NotifyClient();
        }

        try
        {
            written = new FileInfo(writePath).Length;
        }
        catch (IOException)
        {
            // 大きさが取れなくても、書いた分の数え上げで代用する。
        }

        if (written > 0 && !string.Equals(writePath, path, StringComparison.Ordinal))
        {
            // 一時領域に録っていた場合は、ここで最終の保存先へ移す。EPGStation の
            // recordedTmp と同じく、書き込み中は一時領域だけを使い、他の処理からは
            // 完成品しか見えないようにする。
            try
            {
                string? finalDirectory = Path.GetDirectoryName(path);
                if (finalDirectory is { Length: > 0 })
                {
                    Directory.CreateDirectory(finalDirectory);
                }

                File.Move(writePath, path, overwrite: true);
            }
            catch (IOException exception)
            {
                LogTempMoveFailed(logger, writePath, path, exception);
                await store.AbortAsync(recordedId, CancellationToken.None);
                return;
            }
        }

        if (written > 0)
        {
            await store.CompleteAsync(recordedId, videoFileId, written, CancellationToken.None);
            LogRecordingFinished(logger, path, written);
            string? dropLogPath = null;
            if (isEnabledDropCheck)
            {
                dropLogPath = await SaveDropLogAsync(analyzer.Defects);
            }

            // 重複回避の記憶は、ルールで録った番組にだけ要る。手動予約は毎回意図して録るものなので、
            // 次にまた録りたくなっても妨げない。
            if (ruleId is not null)
            {
                await history.AddAsync(
                    name,
                    ChannelId,
                    DateTimeOffset.FromUnixTimeMilliseconds(Interlocked.Read(ref currentEndAt)),
                    CancellationToken.None);
            }

            // 一覧で中身を思い出せるように 1 枚取る。取れなくても録画は成立するので、
            // 失敗しても録画の側は何も変えない。
            try
            {
                await thumbnails.CreateForVideoFileAsync(videoFileId, CancellationToken.None);
            }
            catch (Exception exception)
            {
                LogThumbnailFailed(logger, path, exception);
            }
            hooks.RunRecordedHook(recordingFinishCommand, BuildRecordedPayload(analyzer.Defects, dropLogPath));
            notifier.NotifyClient();
        }
        else
        {
            await store.AbortAsync(recordedId, CancellationToken.None);
            LogRecordingEmpty(logger, path);
        }
    }

    private RecordedHookPayload BuildRecordedPayload(TransportStreamDefects? defects = null, string? dropLogPath = null) => new(
        recordedId,
        programId,
        ChannelId,
        channelName,
        halfWidthChannelName,
        startAt.ToUnixTimeMilliseconds(),
        Interlocked.Read(ref currentEndAt),
        name,
        halfWidthName,
        description,
        halfWidthDescription,
        extended,
        halfWidthExtended,
        RecPath: path,
        LogPath: dropLogPath,
        ErrorCount: defects?.ErrorCount,
        DropCount: defects?.DropCount,
        ScramblingCount: defects?.ScramblingCount,
        ChannelType: channelType);

    /// <summary>
    /// 数えた結果を、人が読める形のファイルと数字の両方で残す。数字だけでは、いつ
    /// どれだけ落ちたのかが分からない。
    /// </summary>
    private async Task<string?> SaveDropLogAsync(TransportStreamDefects defects)
    {
        string? directory = dropLogDirectory is { Length: > 0 } ? dropLogDirectory : Path.GetDirectoryName(path);
        if (directory is null)
        {
            return null;
        }

        string filename = $"{Path.GetFileNameWithoutExtension(path)}.drop.log";
        string dropLogPath = Path.Combine(directory, filename);
        try
        {
            Directory.CreateDirectory(directory);
            await File.WriteAllTextAsync(
                dropLogPath,
                string.Join(
                    '\n',
                    $"file: {Path.GetFileName(path)}",
                    $"error: {defects.ErrorCount.ToString(CultureInfo.InvariantCulture)}",
                    $"drop: {defects.DropCount.ToString(CultureInfo.InvariantCulture)}",
                    $"scrambling: {defects.ScramblingCount.ToString(CultureInfo.InvariantCulture)}",
                    string.Empty),
                CancellationToken.None);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // 書けなくても数字は残す。録画そのものには影響しない。
            LogDropLogFailed(logger, path, exception);
            return null;
        }

        await store.SaveDropLogAsync(recordedId, defects, directory, filename, CancellationToken.None);
        if (defects.DropCount > 0 || defects.ErrorCount > 0 || defects.ScramblingCount > 0)
        {
            LogDefectsFound(logger, path, defects.ErrorCount, defects.DropCount, defects.ScramblingCount);
        }

        return dropLogPath;
    }

    [LoggerMessage(
        EventId = 4013,
        Level = LogLevel.Warning,
        Message = "The recording {Path} has defects: {ErrorCount} errors, {DropCount} drops, {ScramblingCount} scrambled")]
    static partial void LogDefectsFound(
        ILogger logger,
        string path,
        long errorCount,
        long dropCount,
        long scramblingCount);

    [LoggerMessage(
        EventId = 4014,
        Level = LogLevel.Warning,
        Message = "Could not write the drop log next to {Path}")]
    static partial void LogDropLogFailed(ILogger logger, string path, Exception exception);

    [LoggerMessage(
        EventId = 4016,
        Level = LogLevel.Warning,
        Message = "Could not create a thumbnail for {Path}")]
    static partial void LogThumbnailFailed(ILogger logger, string path, Exception exception);

    [LoggerMessage(
        EventId = 4015,
        Level = LogLevel.Error,
        Message = "Could not move the finished recording from the temporary path {TempPath} to {FinalPath}; the recording is aborted")]
    static partial void LogTempMoveFailed(ILogger logger, string tempPath, string finalPath, Exception exception);

    [LoggerMessage(
        EventId = 4010,
        Level = LogLevel.Information,
        Message = "Finished recording {Path} ({Size} bytes)")]
    static partial void LogRecordingFinished(ILogger logger, string path, long size);

    [LoggerMessage(
        EventId = 4011,
        Level = LogLevel.Warning,
        Message = "The recording {Path} lost its feed; what was received is kept")]
    static partial void LogRecordingFailed(ILogger logger, string path, Exception exception);

    [LoggerMessage(
        EventId = 4012,
        Level = LogLevel.Warning,
        Message = "Nothing was received for {Path}; the recording is discarded")]
    static partial void LogRecordingEmpty(ILogger logger, string path);
}
