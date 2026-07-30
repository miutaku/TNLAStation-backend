using Microsoft.Extensions.Options;
using TNLAStation.FfmpegWorker.Options;
using TNLAStation.Infrastructure.Transcoding;

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
    private readonly EncodeDrainState drainState;

    public ProcessGate(IOptions<FfmpegOptions> options, EncodeDrainState drainState)
    {
        int limit = options.Value.EncodeProcessNum;
        semaphore = limit > 0 ? new SemaphoreSlim(limit, limit) : null;
        this.drainState = drainState;
    }

    public async Task<IAsyncDisposable> AcquireAsync(CancellationToken cancellationToken)
    {
        if (!drainState.TryBeginWork())
        {
            throw new OperationCanceledException("The worker is draining.", cancellationToken);
        }

        try
        {
            if (semaphore is null)
            {
                return new Lease(null, drainState);
            }

            await semaphore.WaitAsync(cancellationToken);
            return new Lease(semaphore, drainState);
        }
        catch
        {
            drainState.EndWork();
            throw;
        }
    }

    private sealed class Lease(SemaphoreSlim? semaphore, EncodeDrainState drainState) : IAsyncDisposable
    {
        private int released;

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref released, 1) != 0)
            {
                return ValueTask.CompletedTask;
            }

            semaphore?.Release();
            drainState.EndWork();
            return ValueTask.CompletedTask;
        }
    }
}
