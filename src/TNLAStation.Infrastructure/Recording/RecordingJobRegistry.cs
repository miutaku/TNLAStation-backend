using System.Collections.Concurrent;
using TNLAStation.Application.Abstractions;

namespace TNLAStation.Infrastructure.Recording;

/// <summary>
/// 録画一覧の録画 ID から、実行中の受信ストリームを止められるようにする。
/// Scheduler と HTTP 停止要求が同時に触るため、singleton で共有する。
/// </summary>
internal sealed class RecordingJobRegistry : IRecordingJobRegistry
{
    private readonly ConcurrentDictionary<long, Job> jobs = new();

    public IDisposable Register(RecordingJobIdentity identity, CancellationTokenSource cancellation)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(cancellation);

        var job = new Job(identity, cancellation);
        if (!jobs.TryAdd(identity.RecordedId, job))
        {
            throw new InvalidOperationException($"Recording {identity.RecordedId} is already running.");
        }

        return new Registration(this, identity.RecordedId, job);
    }

    public RecordingJobIdentity? Find(long recordedId) =>
        jobs.TryGetValue(recordedId, out Job? job) ? job.Identity : null;

    public Task? RequestStop(long recordedId)
    {
        if (!jobs.TryGetValue(recordedId, out Job? job))
        {
            return null;
        }

        try
        {
            job.Cancellation.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // セッションの登録解除と同時に来た。Completion を待てば後始末の完了が分かる。
        }

        return job.Completion.Task;
    }

    private void Remove(long recordedId, Job job)
    {
        if (jobs.TryRemove(new KeyValuePair<long, Job>(recordedId, job)))
        {
            job.Completion.TrySetResult();
        }
    }

    private sealed class Job(RecordingJobIdentity identity, CancellationTokenSource cancellation)
    {
        public RecordingJobIdentity Identity { get; } = identity;

        public CancellationTokenSource Cancellation { get; } = cancellation;

        public TaskCompletionSource Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class Registration(RecordingJobRegistry owner, long recordedId, Job job) : IDisposable
    {
        private int disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) == 0)
            {
                owner.Remove(recordedId, job);
            }
        }
    }
}
