using Microsoft.Extensions.Options;
using TNLAStation.FfmpegWorker.Options;
using TNLAStation.Infrastructure.Transcoding;

namespace TNLAStation.FfmpegWorker.Processes;

public enum ProcessPriority
{
    /// <summary>エンコード・サムネイル・probe。空きを待ち、視聴に割り込まれる。</summary>
    Background,

    /// <summary>ライブ視聴と変換配信。人が待っているので、空きが無ければ作る。</summary>
    Viewing,
}

/// <summary>
/// 同時に走らせる ffmpeg/ffprobe の本数を CPU の割り当てから決め、視聴を最優先で通す。
/// プロセス起動はすべてここを通る。
///
/// 視聴は空きが無ければ実行中のエンコードを 1 本止めて枠を空ける。止めたエンコードは
/// 待ち行列へ戻るので、失うのは途中経過だけ。
/// </summary>
public sealed class ProcessGate : IDisposable
{
    private readonly SemaphoreSlim semaphore;
    private readonly EncodeDrainState drainState;
    private readonly Lock gate = new();
    private readonly List<Lease> active = [];

    public ProcessGate(IOptions<FfmpegOptions> options, EncodeDrainState drainState)
    {
        Capacity = ResolveCapacity(options.Value);
        semaphore = new SemaphoreSlim(Capacity, Capacity);
        this.drainState = drainState;
    }

    /// <summary>同時に走らせる本数。</summary>
    public int Capacity { get; }

    /// <summary>視聴が使っている本数。backend はこれを見て受け付けるかどうかを決める。</summary>
    public int ActiveViewing
    {
        get
        {
            lock (gate)
            {
                return active.Count(lease => lease.Priority == ProcessPriority.Viewing);
            }
        }
    }

    /// <summary>
    /// 割り当てられた CPU から決める。.NET の ProcessorCount は cgroup の制限を反映するので、
    /// コンテナでもホストの実コア数にはならない。EPGStation の encodeProcessNum は天井として
    /// 併用する — CPU があっても、それ以上は並べない。
    /// </summary>
    private static int ResolveCapacity(FfmpegOptions options)
    {
        double cost = options.StreamCpuCost > 0 ? options.StreamCpuCost : 1;
        int fromCpu = Math.Max(1, (int)Math.Floor(Environment.ProcessorCount / cost));
        return options.EncodeProcessNum > 0 ? Math.Min(fromCpu, options.EncodeProcessNum) : fromCpu;
    }

    public async Task<ProcessLease> AcquireAsync(ProcessPriority priority, CancellationToken cancellationToken)
    {
        if (!drainState.TryBeginWork())
        {
            throw new OperationCanceledException("The worker is draining.", cancellationToken);
        }

        try
        {
            if (priority == ProcessPriority.Viewing && !semaphore.Wait(0, CancellationToken.None))
            {
                PreemptNewestBackground();
                await semaphore.WaitAsync(cancellationToken);
            }
            else if (priority == ProcessPriority.Background)
            {
                await semaphore.WaitAsync(cancellationToken);
            }

            return Register(priority);
        }
        catch
        {
            drainState.EndWork();
            throw;
        }
    }

    /// <summary>
    /// 新しく始めたものから止める。長く走っているエンコードほど、やり直しで捨てる時間が大きい。
    /// </summary>
    private void PreemptNewestBackground()
    {
        Lease? victim;
        lock (gate)
        {
            victim = active
                .Where(lease => lease.Priority == ProcessPriority.Background)
                .OrderByDescending(lease => lease.StartedAt)
                .FirstOrDefault();
        }

        victim?.Preempt();
    }

    public void Dispose() => semaphore.Dispose();

    private Lease Register(ProcessPriority priority)
    {
        var lease = new Lease(priority, Release);
        lock (gate)
        {
            active.Add(lease);
        }

        return lease;
    }

    private void Release(Lease lease)
    {
        lock (gate)
        {
            active.Remove(lease);
        }

        semaphore.Release();
        drainState.EndWork();
    }

    private sealed class Lease(ProcessPriority priority, Action<Lease> release) : ProcessLease
    {
        private readonly CancellationTokenSource preempted = new();
        private int released;

        public ProcessPriority Priority { get; } = priority;

        public long StartedAt { get; } = Environment.TickCount64;

        public override CancellationToken Preempted => preempted.Token;

        public void Preempt() => preempted.Cancel();

        public override ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref released, 1) != 0)
            {
                return ValueTask.CompletedTask;
            }

            release(this);
            preempted.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}

/// <summary>確保した 1 枠。<see cref="Preempted"/> が立ったら視聴に譲る。</summary>
public abstract class ProcessLease : IAsyncDisposable
{
    public abstract CancellationToken Preempted { get; }

    public abstract ValueTask DisposeAsync();
}
