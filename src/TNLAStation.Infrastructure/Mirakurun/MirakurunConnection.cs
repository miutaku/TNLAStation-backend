using System.IO.Pipes;
using System.Net.Sockets;

namespace TNLAStation.Infrastructure.Mirakurun;

public static class MirakurunConnection
{
    public static HttpMessageHandler CreateHandler(string endpoint, out Uri baseAddress)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(endpoint);

        if (endpoint.StartsWith("http+unix://", StringComparison.OrdinalIgnoreCase))
        {
            string remainder = endpoint["http+unix://".Length..];
            int pathStart = remainder.IndexOf('/');
            string encodedSocketPath = pathStart < 0 ? remainder : remainder[..pathStart];
            string basePath = pathStart < 0 ? "/" : remainder[pathStart..];
            string socketPath = Uri.UnescapeDataString(encodedSocketPath);
            baseAddress = CreateLoopbackBaseAddress(basePath);
            return CreateUnixSocketHandler(socketPath);
        }

        if (endpoint.StartsWith("http://unix:", StringComparison.OrdinalIgnoreCase))
        {
            string remainder = endpoint["http://unix:".Length..];
            int separator = remainder.IndexOf(':');
            string socketPath = separator < 0 ? remainder : remainder[..separator];
            string basePath = separator < 0 ? "/" : remainder[(separator + 1)..];
            baseAddress = CreateLoopbackBaseAddress(basePath);
            return CreateUnixSocketHandler(socketPath);
        }

        if (endpoint.StartsWith("\\\\.\\pipe\\", StringComparison.OrdinalIgnoreCase))
        {
            string pipeName = endpoint["\\\\.\\pipe\\".Length..];
            baseAddress = new Uri("http://localhost/", UriKind.Absolute);
            var handler = CreateDefaultHandler();
            handler.ConnectCallback = async (_, cancellationToken) =>
            {
                var stream = new NamedPipeClientStream(
                    ".",
                    pipeName,
                    PipeDirection.InOut,
                    PipeOptions.Asynchronous);
                await stream.ConnectAsync(cancellationToken);
                return stream;
            };
            return handler;
        }

        baseAddress = EnsureTrailingSlash(new Uri(endpoint, UriKind.Absolute));
        return CreateDefaultHandler();
    }

    private static SocketsHttpHandler CreateUnixSocketHandler(string socketPath)
    {
        var handler = CreateDefaultHandler();
        handler.ConnectCallback = async (_, cancellationToken) =>
        {
            var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            try
            {
                await socket.ConnectAsync(new UnixDomainSocketEndPoint(socketPath), cancellationToken);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        };
        return handler;
    }

    private static SocketsHttpHandler CreateDefaultHandler() =>
        new()
        {
            AutomaticDecompression = System.Net.DecompressionMethods.All,
            PooledConnectionLifetime = TimeSpan.FromMinutes(10)
        };

    private static Uri CreateLoopbackBaseAddress(string basePath)
    {
        string trimmed = basePath.Trim('/');
        string normalized = trimmed.Length == 0 ? "/" : $"/{trimmed}/";
        return new Uri($"http://localhost{normalized}", UriKind.Absolute);
    }

    private static Uri EnsureTrailingSlash(Uri uri) =>
        uri.AbsoluteUri.EndsWith('/')
            ? uri
            : new Uri(uri.AbsoluteUri + "/", UriKind.Absolute);
}
