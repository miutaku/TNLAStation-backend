using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using TNLAStation.Infrastructure.Configuration;

namespace TNLAStation.Infrastructure.Streaming;

/// <summary>readyなストリーミングworkerを開始要求ごとに巡回する。</summary>
public sealed class StreamingWorkerSelector(IOptions<FfmpegWorkerOptions> options)
{
    private readonly Dictionary<string, double> nodeWeights = options.Value.StreamingNodeWeights;
    private readonly Uri[] candidates = options.Value.StreamingBaseUrls
        .Where(value => !string.IsNullOrWhiteSpace(value))
        .Select(value => new Uri(EnsureTrailingSlash(value), UriKind.Absolute))
        .ToArray();
    private long next = -1;

    public async Task<Uri?> SelectAsync(HttpClient client, CancellationToken cancellationToken)
    {
        if (candidates.Length == 0)
        {
            return client.BaseAddress;
        }

        WorkerHealth[] ready = await ReadReadyWorkersAsync(client, cancellationToken);
        if (ready.Length == 0)
        {
            int fallbackIndex = (int)((ulong)Interlocked.Increment(ref next) % (uint)candidates.Length);
            return candidates[fallbackIndex];
        }

        double minimumNodeLoad = ready
            .GroupBy(item => item.NodeName)
            .Min(group => group.Sum(item => item.ActiveCount) / ResolveNodeWeight(group.Key));
        string[] leastLoadedNodes = ready
            .GroupBy(item => item.NodeName)
            .Where(group => Math.Abs(
                (group.Sum(item => item.ActiveCount) / ResolveNodeWeight(group.Key)) - minimumNodeLoad) < double.Epsilon)
            .Select(group => group.Key)
            .Order(StringComparer.Ordinal)
            .ToArray();
        long selection = Interlocked.Increment(ref next);
        string selectedNode = leastLoadedNodes[(int)((ulong)selection % (uint)leastLoadedNodes.Length)];
        WorkerHealth[] nodeWorkers = ready
            .Where(item => item.NodeName == selectedNode)
            .ToArray();
        int minimumActiveCount = nodeWorkers.Min(item => item.ActiveCount);
        WorkerHealth[] leastBusy = nodeWorkers
            .Where(item => item.ActiveCount == minimumActiveCount)
            .ToArray();
        int selectedIndex = (int)((ulong)selection % (uint)leastBusy.Length);
        return leastBusy[selectedIndex].BaseAddress;
    }

    private async Task<WorkerHealth[]> ReadReadyWorkersAsync(
        HttpClient client,
        CancellationToken cancellationToken)
    {
        WorkerHealth?[] results = await Task.WhenAll(
            candidates.Select(candidate => ReadHealthAsync(client, candidate, cancellationToken)));
        return results.OfType<WorkerHealth>().ToArray();
    }

    private static async Task<WorkerHealth?> ReadHealthAsync(
        HttpClient client,
        Uri candidate,
        CancellationToken cancellationToken)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(2));
            using HttpResponseMessage response = await client.GetAsync(
                new Uri(candidate, "health"),
                HttpCompletionOption.ResponseHeadersRead,
                timeout.Token);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            HealthResponse? health = await response.Content.ReadFromJsonAsync<HealthResponse>(
                cancellationToken: timeout.Token);
            string nodeName = string.IsNullOrWhiteSpace(health?.NodeName)
                ? candidate.Host
                : health.NodeName;
            return new WorkerHealth(candidate, nodeName, Math.Max(0, health?.ActiveCount ?? 0));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        catch (HttpRequestException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private double ResolveNodeWeight(string nodeName) =>
        nodeWeights.TryGetValue(nodeName, out double weight) && weight > 0 ? weight : 1;

    private static string EnsureTrailingSlash(string value) =>
        value.EndsWith('/') ? value : $"{value}/";

    private sealed record HealthResponse(int ActiveCount, string? NodeName);

    private sealed record WorkerHealth(Uri BaseAddress, string NodeName, int ActiveCount);
}
