using System.Net;
using Microsoft.Extensions.Options;
using TNLAStation.Infrastructure.Configuration;
using TNLAStation.Infrastructure.Streaming;

namespace TNLAStation.Infrastructure.Tests;

public sealed class StreamingWorkerSelectorTests
{
    [Fact]
    public async Task SelectsReadyWorkersInRoundRobinOrder()
    {
        var selector = CreateSelector();
        using var client = new HttpClient(new HealthHandler(_ => HttpStatusCode.OK));

        Uri? first = await selector.SelectAsync(client, CancellationToken.None);
        Uri? second = await selector.SelectAsync(client, CancellationToken.None);
        Uri? third = await selector.SelectAsync(client, CancellationToken.None);

        Assert.Equal("worker-0", first?.Host);
        Assert.Equal("worker-1", second?.Host);
        Assert.Equal("worker-2", third?.Host);
    }

    [Fact]
    public async Task SkipsWorkersThatAreNotReady()
    {
        var selector = CreateSelector();
        using var client = new HttpClient(new HealthHandler(
            host => host == "worker-1" ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK));

        _ = await selector.SelectAsync(client, CancellationToken.None);
        Uri? selected = await selector.SelectAsync(client, CancellationToken.None);

        Assert.Equal("worker-2", selected?.Host);
    }

    private static StreamingWorkerSelector CreateSelector() =>
        new(Options.Create(new FfmpegWorkerOptions
        {
            StreamingBaseUrls =
            [
                "http://worker-0:8080",
                "http://worker-1:8080",
                "http://worker-2:8080",
            ],
        }));

    private sealed class HealthHandler(Func<string, HttpStatusCode> status) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(status(request.RequestUri!.Host)));
    }
}
