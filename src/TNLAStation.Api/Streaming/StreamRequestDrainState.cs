namespace TNLAStation.Api.Streaming;

/// <summary>ローリング更新前に新しい直結配信を止め、既存の応答完了を待つ。</summary>
public sealed class StreamRequestDrainState
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

    public bool TryBegin()
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

    public void End()
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
