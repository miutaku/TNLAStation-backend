using TNLAStation.Infrastructure.Transcoding;

namespace TNLAStation.Infrastructure.Tests;

public sealed class EncodeJobRegistryTests
{
    [Fact]
    public async Task CancellationCompletesOnlyAfterTheWorkerUnregisters()
    {
        var registry = new EncodeJobRegistry();
        using var cancellation = new CancellationTokenSource();
        IDisposable registration = registry.Register(42, cancellation);

        Task? requested = registry.RequestCancel(42);
        Assert.NotNull(requested);
        Task completion = requested;

        Assert.True(cancellation.IsCancellationRequested);
        Assert.False(completion.IsCompleted);

        registration.Dispose();
        await completion.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.True(completion.IsCompletedSuccessfully);
    }

    [Fact]
    public void AnUnknownOrFinishedJobHasNothingToCancel()
    {
        var registry = new EncodeJobRegistry();
        using var cancellation = new CancellationTokenSource();

        Assert.Null(registry.RequestCancel(7));

        using (registry.Register(7, cancellation))
        {
        }

        Assert.Null(registry.RequestCancel(7));
    }

    [Fact]
    public void TheSameTaskCannotBeRegisteredTwice()
    {
        var registry = new EncodeJobRegistry();
        using var firstCancellation = new CancellationTokenSource();
        using var secondCancellation = new CancellationTokenSource();
        using IDisposable registration = registry.Register(9, firstCancellation);

        Assert.Throws<InvalidOperationException>(() => registry.Register(9, secondCancellation));
    }
}
