using TNLAStation.FfmpegWorker.Options;
using TNLAStation.FfmpegWorker.Processes;
using TNLAStation.Infrastructure.Transcoding;

namespace TNLAStation.FfmpegWorker.Tests;

public sealed class ProcessGateCapacityTests
{
    private static ProcessGate Create(FfmpegOptions options) =>
        new(Microsoft.Extensions.Options.Options.Create(options), new EncodeDrainState());

    /// <summary>encodeProcessNum は天井。CPU に余裕があっても、それ以上は並べない。</summary>
    [Fact]
    public void AnExplicitProcessCountCapsTheCpuDerivedCapacity()
    {
        using ProcessGate gate = Create(new FfmpegOptions { EncodeProcessNum = 1, StreamCpuCost = 0.1 });

        Assert.Equal(1, gate.Capacity);
    }

    /// <summary>逆に CPU が足りなければ、encodeProcessNum が大きくても増やさない。</summary>
    [Fact]
    public void AGenerousProcessCountDoesNotOverrideTheCpuAllowance()
    {
        using ProcessGate gate = Create(new FfmpegOptions { EncodeProcessNum = 99, StreamCpuCost = 1.2 });

        Assert.Equal(Math.Max(1, (int)Math.Floor(Environment.ProcessorCount / 1.2)), gate.Capacity);
    }

    /// <summary>
    /// 上限を決めるのは割り当て CPU。コンテナでも .NET は cgroup の制限を返すので、
    /// ホストの実コア数にはならない。
    /// </summary>
    [Fact]
    public void WithoutAnExplicitCountTheCapacityComesFromTheCpuAllowance()
    {
        using ProcessGate gate = Create(new FfmpegOptions { StreamCpuCost = 1.2 });

        Assert.Equal(Math.Max(1, (int)Math.Floor(Environment.ProcessorCount / 1.2)), gate.Capacity);
    }

    [Fact]
    public void ACostLargerThanTheMachineStillLeavesOneSlot()
    {
        using ProcessGate gate = Create(new FfmpegOptions { StreamCpuCost = Environment.ProcessorCount * 4.0 });

        Assert.Equal(1, gate.Capacity);
    }

    /// <summary>視聴は人が待っている。埋まっていれば実行中のエンコードを止めて割り込む。</summary>
    [Fact]
    public async Task ViewingTakesTheSlotOfARunningEncode()
    {
        using ProcessGate gate = Create(new FfmpegOptions { EncodeProcessNum = 1 });
        await using ProcessLease encode = await gate.AcquireAsync(ProcessPriority.Background, CancellationToken.None);

        Task<ProcessLease> viewing = gate.AcquireAsync(ProcessPriority.Viewing, CancellationToken.None);

        // 割り込みを受けた側が畳むまで枠は空かない。実処理では ffmpeg を止めて lease を返す。
        Assert.True(encode.Preempted.IsCancellationRequested);
        Assert.False(viewing.IsCompleted);
        await encode.DisposeAsync();
        await using ProcessLease acquired = await viewing;
        Assert.Equal(1, gate.ActiveViewing);
    }

    /// <summary>視聴どうしは奪い合わない。奪えば見ている人の映像が切れる。</summary>
    [Fact]
    public async Task ViewingNeverPreemptsAnotherViewing()
    {
        using ProcessGate gate = Create(new FfmpegOptions { EncodeProcessNum = 1 });
        await using ProcessLease first = await gate.AcquireAsync(ProcessPriority.Viewing, CancellationToken.None);

        Task<ProcessLease> second = gate.AcquireAsync(ProcessPriority.Viewing, CancellationToken.None);

        Assert.False(first.Preempted.IsCancellationRequested);
        Assert.False(second.IsCompleted);
    }

    /// <summary>
    /// エンコードは枠を掴んだまま待たない。待つと待ち行列側では実行中に見えてしまう。
    /// </summary>
    [Fact]
    public async Task TryAcquireGivesUpImmediatelyWhenEverySlotIsTaken()
    {
        using ProcessGate gate = Create(new FfmpegOptions { EncodeProcessNum = 1 });
        await using ProcessLease viewing = await gate.AcquireAsync(ProcessPriority.Viewing, CancellationToken.None);

        Assert.Null(gate.TryAcquire(ProcessPriority.Background));
    }

    [Fact]
    public void TryAcquireTakesAFreeSlot()
    {
        using ProcessGate gate = Create(new FfmpegOptions { EncodeProcessNum = 1 });

        ProcessLease? lease = gate.TryAcquire(ProcessPriority.Background);

        Assert.NotNull(lease);
        Assert.Null(gate.TryAcquire(ProcessPriority.Background));
    }

    [Fact]
    public async Task BackgroundWorkWaitsForARealSlotInsteadOfPreempting()
    {
        using ProcessGate gate = Create(new FfmpegOptions { EncodeProcessNum = 1 });
        await using ProcessLease running = await gate.AcquireAsync(ProcessPriority.Background, CancellationToken.None);

        Task<ProcessLease> queued = gate.AcquireAsync(ProcessPriority.Background, CancellationToken.None);

        Assert.False(running.Preempted.IsCancellationRequested);
        Assert.False(queued.IsCompleted);
    }
}
