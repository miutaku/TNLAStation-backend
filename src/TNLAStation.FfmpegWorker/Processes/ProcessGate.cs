using Microsoft.Extensions.Options;
using TNLAStation.FfmpegWorker.Options;

namespace TNLAStation.FfmpegWorker.Processes;

/// <summary>
/// 同時に起動する ffmpeg/ffprobe プロセス数の上限を守る (EPGStation の encodeProcessNum 相当)。
/// エンコード・サムネイル抽出・probe・HLS 配信・変換配信、すべてのプロセス起動がここを通る。
/// <see cref="FfmpegOptions.EncodeProcessNum"/> が 0 (既定) なら無制限。
///
/// EPGStation は上限に達すると優先度の低いプロセスを kill して割り込ませるが、ここでは
/// 単純化して空きが出るまで FIFO で待つ。長時間 HLS/変換配信を続ける枠が空かないと後続が
/// 待たされ続ける点が異なるが、既定値 (無制限) では影響しない。
/// </summary>
public sealed class ProcessGate
{
    private readonly SemaphoreSlim? semaphore;

    public ProcessGate(IOptions<FfmpegOptions> options)
    {
        int limit = options.Value.EncodeProcessNum;
        semaphore = limit > 0 ? new SemaphoreSlim(limit, limit) : null;
    }

    public async Task<IAsyncDisposable> AcquireAsync(CancellationToken cancellationToken)
    {
        if (semaphore is null)
        {
            return NullLease.Instance;
        }

        await semaphore.WaitAsync(cancellationToken);
        return new Lease(semaphore);
    }

    private sealed class Lease(SemaphoreSlim semaphore) : IAsyncDisposable
    {
        private int released;

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref released, 1) == 0)
            {
                semaphore.Release();
            }

            return ValueTask.CompletedTask;
        }
    }

    private sealed class NullLease : IAsyncDisposable
    {
        public static readonly NullLease Instance = new();

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
