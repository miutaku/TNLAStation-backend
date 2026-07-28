using TNLAStation.Application.Abstractions;
using TNLAStation.Infrastructure.Recording;

namespace TNLAStation.Infrastructure.Tests;

public sealed class RecordingJobRegistryTests
{
    [Fact]
    public async Task StopCompletesOnlyAfterTheRecordingHasBeenFinalized()
    {
        var registry = new RecordingJobRegistry();
        using var cancellation = new CancellationTokenSource();
        var identity = new RecordingJobIdentity(11, 22, "manual:33", 33);
        IDisposable registration = registry.Register(identity, cancellation);

        Assert.Equal(identity, registry.Find(identity.RecordedId));
        Task? requested = registry.RequestStop(identity.RecordedId);
        Assert.NotNull(requested);
        Task completion = requested;

        Assert.True(cancellation.IsCancellationRequested);
        Assert.False(completion.IsCompleted);

        registration.Dispose();
        await completion.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.Null(registry.Find(identity.RecordedId));
    }

    [Fact]
    public void UnknownAndFinishedRecordingsCannotBeStoppedAgain()
    {
        var registry = new RecordingJobRegistry();
        using var cancellation = new CancellationTokenSource();
        var identity = new RecordingJobIdentity(1, 2, "rule:3:4", null);

        Assert.Null(registry.RequestStop(identity.RecordedId));
        using (registry.Register(identity, cancellation))
        {
        }

        Assert.Null(registry.RequestStop(identity.RecordedId));
    }

    [Fact]
    public void TheSameRecordedIdCannotBeRegisteredTwice()
    {
        var registry = new RecordingJobRegistry();
        using var firstCancellation = new CancellationTokenSource();
        using var secondCancellation = new CancellationTokenSource();
        var identity = new RecordingJobIdentity(1, 2, "rule:3:4", null);
        using IDisposable registration = registry.Register(identity, firstCancellation);

        Assert.Throws<InvalidOperationException>(() => registry.Register(identity, secondCancellation));
    }
}
