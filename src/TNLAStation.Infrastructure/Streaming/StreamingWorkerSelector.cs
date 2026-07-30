using Microsoft.Extensions.Options;
using TNLAStation.Infrastructure.Configuration;

namespace TNLAStation.Infrastructure.Streaming;

/// <summary>readyなストリーミングworkerを開始要求ごとに巡回する。</summary>
public sealed class StreamingWorkerSelector(IOptions<FfmpegWorkerOptions> options)
{
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

        int start = (int)((ulong)Interlocked.Increment(ref next) % (uint)candidates.Length);
        for (int offset = 0; offset < candidates.Length; offset++)
        {
            Uri candidate = candidates[(start + offset) % candidates.Length];
            if (await IsReadyAsync(client, candidate, cancellationToken))
            {
                return candidate;
            }
        }

        return candidates[start];
    }

    private static async Task<bool> IsReadyAsync(
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
            return response.IsSuccessStatusCode;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
        catch (HttpRequestException)
        {
            return false;
        }
    }

    private static string EnsureTrailingSlash(string value) =>
        value.EndsWith('/') ? value : $"{value}/";
}
