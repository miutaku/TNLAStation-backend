using System.Net;
using System.Net.Http.Json;
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
        using var client = new HttpClient(new HealthHandler(
            host => new HealthResult(HttpStatusCode.OK, 0, host)));

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
            host => new HealthResult(
                host == "worker-1" ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK,
                0,
                host)));

        _ = await selector.SelectAsync(client, CancellationToken.None);
        Uri? selected = await selector.SelectAsync(client, CancellationToken.None);

        Assert.Equal("worker-2", selected?.Host);
    }

    [Fact]
    public async Task SelectsTheNodeWithTheLowestCapacityWeightedLoad()
    {
        var selector = new StreamingWorkerSelector(Options.Create(new FfmpegWorkerOptions
        {
            StreamingBaseUrls = ["http://worker-0:8080", "http://worker-1:8080"],
            StreamingNodeWeights =
            {
                ["node-5950x"] = 16,
                ["node-5600x"] = 6,
            },
        }));
        using var client = new HttpClient(new HealthHandler(host => host switch
        {
            "worker-0" => new HealthResult(HttpStatusCode.OK, 2, "node-5950x"),
            _ => new HealthResult(HttpStatusCode.OK, 1, "node-5600x"),
        }));

        Uri? selected = await selector.SelectAsync(client, CancellationToken.None);

        Assert.Equal("worker-0", selected?.Host);
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

    private sealed class HealthHandler(Func<string, HealthResult> resultFactory) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            HealthResult result = resultFactory(request.RequestUri!.Host);
            return Task.FromResult(new HttpResponseMessage(result.StatusCode)
            {
                Content = JsonContent.Create(new
                {
                    status = "ok",
                    activeCount = result.ActiveCount,
                    nodeName = result.NodeName,
                }),
            });
        }
    }

    private sealed record HealthResult(HttpStatusCode StatusCode, int ActiveCount, string NodeName);
}
