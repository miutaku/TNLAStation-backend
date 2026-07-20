using TNLAStation.Application.Abstractions;

namespace TNLAStation.Infrastructure.Streaming;

/// <summary>
/// Mirakurun が設定されていない構成。視聴だけができないので、その理由をそのまま返す。
/// </summary>
public sealed class UnavailableLiveStreamService : ILiveStreamService
{
    public ValueTask<long> StartHlsAsync(long channelId, int mode, CancellationToken cancellationToken) =>
        throw new LiveStreamException("MirakurunIsNotConfigured");

    public bool Keep(long streamId) => false;

    public ValueTask<bool> StopAsync(long streamId) => ValueTask.FromResult(false);
}
