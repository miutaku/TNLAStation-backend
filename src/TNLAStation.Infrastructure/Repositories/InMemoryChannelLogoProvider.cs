using TNLAStation.Application.Abstractions;

namespace TNLAStation.Infrastructure.Repositories;

public sealed class InMemoryChannelLogoProvider : IChannelLogoProvider
{
    private static readonly byte[] OnePixelPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");

    public ValueTask<ReadOnlyMemory<byte>> GetLogoAsync(long channelId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult<ReadOnlyMemory<byte>>(OnePixelPng);
    }
}
