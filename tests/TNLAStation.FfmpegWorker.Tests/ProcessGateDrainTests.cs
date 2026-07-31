using TNLAStation.FfmpegWorker.Options;
using TNLAStation.FfmpegWorker.Processes;
using TNLAStation.Infrastructure.Transcoding;

namespace TNLAStation.FfmpegWorker.Tests;

public sealed class ProcessGateDrainTests
{
    [Fact]
    public async Task DrainWaitsForRunningProcessAndRejectsAnotherProcess()
    {
        var drainState = new EncodeDrainState();
        var gate = new ProcessGate(
            Microsoft.Extensions.Options.Options.Create(new FfmpegOptions()),
            drainState);
        ProcessLease running = await gate.AcquireAsync(ProcessPriority.Background, CancellationToken.None);

        Task drain = drainState.DrainAsync(CancellationToken.None);

        Assert.False(drain.IsCompleted);
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => gate.AcquireAsync(ProcessPriority.Background, CancellationToken.None));

        await running.DisposeAsync();
        await drain;
    }
}
