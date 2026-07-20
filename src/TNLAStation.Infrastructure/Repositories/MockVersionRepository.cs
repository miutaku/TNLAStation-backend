using TNLAStation.Application.Abstractions;

namespace TNLAStation.Infrastructure.Repositories;

public sealed class MockVersionRepository : IVersionRepository
{
    public ValueTask<string> GetAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult("2.10.0");
    }
}
