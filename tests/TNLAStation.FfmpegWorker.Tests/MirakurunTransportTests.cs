using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace TNLAStation.FfmpegWorker.Tests;

public sealed class MirakurunTransportTests
{
    [Theory]
    [InlineData("http+unix")]
    [InlineData("http://unix")]
    [InlineData("http")]
    public async Task LiveStreamUsesTheConfiguredTransport(string transport)
    {
        if (!Socket.OSSupportsUnixDomainSockets || OperatingSystem.IsWindows())
        {
            return;
        }

        string socketPath = Path.Combine(Path.GetTempPath(), $"tnla-{Guid.NewGuid():N}.sock");
        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.ConfigureKestrel(options =>
        {
            if (transport == "http")
            {
                options.Listen(IPAddress.Loopback, 0);
                return;
            }

            options.ListenUnixSocket(socketPath);
        });
        await using WebApplication tuner = builder.Build();
        byte[] payload = "mirakurun-stream"u8.ToArray();
        string? receivedPriority = null;
        tuner.MapGet("/api/services/1/stream", (HttpContext context) =>
        {
            receivedPriority = context.Request.Headers["X-Mirakurun-Priority"].ToString();
            Assert.Equal("1", context.Request.Query["decode"].ToString());
            return Results.Bytes(payload, "video/MP2T");
        });

        try
        {
            await tuner.StartAsync();
            string endpoint = transport switch
            {
                "http+unix" => $"http+unix://{Uri.EscapeDataString(socketPath)}/",
                "http://unix" => $"http://unix:{socketPath}:/",
                _ => tuner.Urls.Single(),
            };
            await using var worker = new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
                host.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                    new Dictionary<string, string?> { ["Mirakurun:BaseUrl"] = endpoint })));
            using HttpClient client = worker.CreateClient();
            using HttpResponseMessage response = await client.PostAsJsonAsync("/streams/transcode/live", new
            {
                channelId = 1,
                height = 480,
                videoBitrate = "1500k",
                audioBitrate = "128k",
                formatArguments = Array.Empty<string>(),
                priority = 1,
                command = "/bin/cat",
            });

            response.EnsureSuccessStatusCode();
            Assert.Equal(payload, await response.Content.ReadAsByteArrayAsync());
            Assert.Equal("1", receivedPriority);
        }
        finally
        {
            await tuner.StopAsync();
            File.Delete(socketPath);
        }
    }
}
