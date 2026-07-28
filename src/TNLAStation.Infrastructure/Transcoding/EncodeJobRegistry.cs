using System.Collections.Concurrent;
using TNLAStation.Application.Abstractions;

namespace TNLAStation.Infrastructure.Transcoding;

/// <summary>
/// 実行中のエンコードを ID で引けるようにして、取り消し要求で ffmpeg を止められるようにする。
/// ワーカーと待ち行列 (取り消し経路) の両方が触るので singleton で 1 つだけ持つ。
/// </summary>
internal sealed class EncodeJobRegistry : IEncodeJobRegistry
{
    private readonly ConcurrentDictionary<long, Job> jobs = new();

    public IDisposable Register(long taskId, CancellationTokenSource cancellation)
    {
        ArgumentNullException.ThrowIfNull(cancellation);
        var job = new Job(cancellation);
        if (!jobs.TryAdd(taskId, job))
        {
            throw new InvalidOperationException($"Encode task {taskId} is already running.");
        }

        return new Registration(this, taskId, job);
    }

    public Task? RequestCancel(long taskId)
    {
        if (!jobs.TryGetValue(taskId, out Job? job))
        {
            return null;
        }

        try
        {
            job.Cancellation.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // 登録解除と同時に来た。Completion はそのまま待てばよい。
        }

        return job.Completion.Task;
    }

    private void Remove(long taskId, Job job)
    {
        if (jobs.TryRemove(new KeyValuePair<long, Job>(taskId, job)))
        {
            job.Completion.TrySetResult();
        }
    }

    private sealed class Job(CancellationTokenSource cancellation)
    {
        public CancellationTokenSource Cancellation { get; } = cancellation;

        public TaskCompletionSource Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class Registration(EncodeJobRegistry owner, long taskId, Job job) : IDisposable
    {
        private int disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) == 0)
            {
                owner.Remove(taskId, job);
            }
        }
    }
}
