using TNLAStation.Application.Abstractions;

namespace TNLAStation.Infrastructure.Repositories;

public sealed class InMemoryChannelLogoProvider : IChannelLogoProvider
{
    public ValueTask<ReadOnlyMemory<byte>> GetLogoAsync(long channelId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        throw new InvalidOperationException("MirakurunIsNotConfigured");
    }
}
