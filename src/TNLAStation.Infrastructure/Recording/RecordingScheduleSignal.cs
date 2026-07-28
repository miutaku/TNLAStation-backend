using System.Threading.Channels;
using TNLAStation.Application.Abstractions;

namespace TNLAStation.Infrastructure.Recording;

/// <summary>
/// 予約表が変わったことを録画スケジューラへ伝える、取りこぼしのない 1 要素の通知。
/// 通知が重なっても「少なくとも 1 回見直す」という意味は同じなので、1 件にまとめる。
/// </summary>
public sealed class RecordingScheduleSignal : IRecordingScheduleTrigger
{
    private readonly Channel<byte> requests = Channel.CreateBounded<byte>(
        new BoundedChannelOptions(1)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.DropWrite,
            AllowSynchronousContinuations = false,
        });

    public ValueTask RequestAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        requests.Writer.TryWrite(0);
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// 通知または定期確認時刻の早いほうまで待つ。待ち手のいない間に来た通知も Channel に
    /// 残るため、起動・再生成の順序に左右されない。
    /// </summary>
    public async ValueTask WaitAsync(
        TimeSpan pollInterval,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        using var waitCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Task signal = requests.Reader.ReadAsync(waitCancellation.Token).AsTask();
        Task poll = Task.Delay(pollInterval, timeProvider, waitCancellation.Token);

        Task completed = await Task.WhenAny(signal, poll);
        try
        {
            await completed;
        }
        finally
        {
            // 負けた待機を残すと、次の通知を古い待ち手が横取りする。
            waitCancellation.Cancel();
        }
    }
}
