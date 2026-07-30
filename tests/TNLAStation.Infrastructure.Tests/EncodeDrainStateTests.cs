using TNLAStation.Infrastructure.Transcoding;

namespace TNLAStation.Infrastructure.Tests;

public sealed class EncodeDrainStateTests
{
    [Fact]
    public async Task DrainCompletesImmediatelyWhenIdle()
    {
        var state = new EncodeDrainState();

        await state.DrainAsync(CancellationToken.None);

        Assert.True(state.IsDraining);
        Assert.False(state.TryBeginWork());
    }

    [Fact]
    public async Task DrainWaitsForActiveWorkAndRejectsNewWork()
    {
        var state = new EncodeDrainState();
        Assert.True(state.TryBeginWork());

        Task drain = state.DrainAsync(CancellationToken.None);

        Assert.False(drain.IsCompleted);
        Assert.False(state.TryBeginWork());

        state.EndWork();
        await drain;
    }
}
