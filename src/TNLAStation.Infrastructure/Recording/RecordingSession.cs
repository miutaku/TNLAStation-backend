using Microsoft.Extensions.Logging;
using TNLAStation.Application.Abstractions;
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
    string path,
    IMirakurunClient mirakurun,
    IRecordingStore store,
    IThumbnailService thumbnails,
    ILogger logger) : IDisposable
{
    private readonly CancellationTokenSource lifetime = new();
    private Task? worker;

    public long ReserveId => reserveId;

    public bool IsRunning => worker is { IsCompleted: false };

    public void Start() => worker = Task.Run(() => RunAsync(lifetime.Token), CancellationToken.None);

    public async Task StopAsync()
    {
        await lifetime.CancelAsync();
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

        Dispose();
    }

    public void Dispose() => lifetime.Dispose();

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        long written = 0;
        try
        {
            await using Stream source = await mirakurun.OpenServiceStreamAsync(channelId, cancellationToken);
            await using var destination = new FileStream(
                path,
                FileMode.Create,
                FileAccess.Write,
                FileShare.Read,
                bufferSize: 1 << 16,
                useAsync: true);

            byte[] buffer = new byte[1 << 16];
            while (true)
            {
                int read = await source.ReadAsync(buffer, cancellationToken);
                if (read == 0)
                {
                    break;
                }

                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                written += read;
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
        }

        try
        {
            written = new FileInfo(path).Length;
        }
        catch (IOException)
        {
            // 大きさが取れなくても、書いた分の数え上げで代用する。
        }

        if (written > 0)
        {
            await store.CompleteAsync(recordedId, videoFileId, written, CancellationToken.None);
            LogRecordingFinished(logger, path, written);

            // 一覧で中身を思い出せるように 1 枚取る。取れなくても録画は成立するので、
            // 失敗しても録画の側は何も変えない。
            await thumbnails.CreateForVideoFileAsync(videoFileId, CancellationToken.None);
        }
        else
        {
            await store.AbortAsync(recordedId, CancellationToken.None);
            LogRecordingEmpty(logger, path);
        }
    }

    [LoggerMessage(
        EventId = 4010,
        Level = LogLevel.Information,
        Message = "Finished recording {Path} ({Size} bytes)")]
    private static partial void LogRecordingFinished(ILogger logger, string path, long size);

    [LoggerMessage(
        EventId = 4011,
        Level = LogLevel.Warning,
        Message = "The recording {Path} lost its feed; what was received is kept")]
    private static partial void LogRecordingFailed(ILogger logger, string path, Exception exception);

    [LoggerMessage(
        EventId = 4012,
        Level = LogLevel.Warning,
        Message = "Nothing was received for {Path}; the recording is discarded")]
    private static partial void LogRecordingEmpty(ILogger logger, string path);
}
