using TNLAStation.Application.Abstractions;

namespace TNLAStation.Infrastructure.Repositories;

public sealed class InMemoryEpgSyncLeaseProvider : IEpgSyncLeaseProvider, IDisposable
{
    private readonly SemaphoreSlim semaphore = new(1, 1);

    public async ValueTask<IAsyncDisposable?> TryAcquireAsync(CancellationToken cancellationToken)
    {
        bool acquired = await semaphore.WaitAsync(TimeSpan.Zero, cancellationToken);
        return acquired ? new Lease(semaphore) : null;
    }

    public void Dispose() => semaphore.Dispose();

    private sealed class Lease(SemaphoreSlim semaphore) : IAsyncDisposable
    {
        private bool disposed;

        public ValueTask DisposeAsync()
        {
            if (!disposed)
            {
                disposed = true;
                semaphore.Release();
            }

            return ValueTask.CompletedTask;
        }
    }
}
