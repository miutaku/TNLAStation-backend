namespace TNLAStation.Infrastructure.Transcoding;

/// <summary>
/// Pod終了前に新しい処理の受付を止め、実行中の処理が完了するまで待つ。
/// </summary>
public sealed class EncodeDrainState
{
    private readonly Lock gate = new();
    private TaskCompletionSource? drained;
    private bool isDraining;
    private int activeCount;

    public bool IsDraining
    {
        get
        {
            lock (gate)
            {
                return isDraining;
            }
        }
    }

    public bool TryBeginWork()
    {
        lock (gate)
        {
            if (isDraining)
            {
                return false;
            }

            activeCount++;
            return true;
        }
    }

    public void EndWork()
    {
        TaskCompletionSource? completion = null;
        lock (gate)
        {
            activeCount--;
            if (!isDraining || activeCount != 0)
            {
                return;
            }

            completion = drained;
        }

        completion?.TrySetResult();
    }

    public Task DrainAsync(CancellationToken cancellationToken)
    {
        Task completion;
        lock (gate)
        {
            isDraining = true;
            if (activeCount == 0)
            {
                return Task.CompletedTask;
            }

            drained ??= new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            completion = drained.Task;
        }

        return completion.WaitAsync(cancellationToken);
    }
}
