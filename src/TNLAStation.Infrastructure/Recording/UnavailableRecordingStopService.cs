using TNLAStation.Application.Abstractions;

namespace TNLAStation.Infrastructure.Recording;

/// <summary>録画保存先を持たない構成では、停止できる録画も存在しない。</summary>
internal sealed class UnavailableRecordingStopService : IRecordingStopService
{
    public ValueTask<bool> StopAsync(long recordedId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(false);
    }
}
