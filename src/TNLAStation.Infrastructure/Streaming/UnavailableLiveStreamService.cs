using TNLAStation.Application.Abstractions;

namespace TNLAStation.Infrastructure.Streaming;

/// <summary>
/// Mirakurun が設定されていない構成。視聴だけができないので、その理由をそのまま返す。
/// </summary>
public sealed class UnavailableLiveStreamService : ILiveStreamService
{
    public ValueTask<long> StartHlsAsync(long channelId, int mode, CancellationToken cancellationToken) =>
        throw new LiveStreamException("MirakurunIsNotConfigured");

    public ValueTask<LowLatencyPlayback> StartLowLatencyAsync(long channelId, int mode, CancellationToken cancellationToken) =>
        throw new LiveStreamException("MirakurunIsNotConfigured");

    public ValueTask<long> StartRecordedHlsAsync(
        long videoFileId,
        double playPosition,
        int mode,
        CancellationToken cancellationToken) =>
        throw new LiveStreamException("MirakurunIsNotConfigured");

    public bool Keep(long streamId) => false;

    public ValueTask<bool> StopAsync(long streamId) => ValueTask.FromResult(false);

    public ValueTask StopAllAsync() => ValueTask.CompletedTask;

    public ValueTask<Stream> OpenLiveStreamAsync(long channelId, int mode, CancellationToken cancellationToken) =>
        throw new LiveStreamException("MirakurunIsNotConfigured");

    public ValueTask<TranscodedOutput> OpenTranscodedLiveAsync(
        long channelId,
        string format,
        int mode,
        CancellationToken cancellationToken) =>
        throw new LiveStreamException("MirakurunIsNotConfigured");

    public ValueTask<TranscodedOutput> OpenTranscodedRecordedAsync(
        long videoFileId,
        string format,
        int mode,
        double playPosition,
        CancellationToken cancellationToken) =>
        throw new LiveStreamException("MirakurunIsNotConfigured");
}
