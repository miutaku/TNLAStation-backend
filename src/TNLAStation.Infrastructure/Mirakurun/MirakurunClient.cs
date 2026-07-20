using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TNLAStation.Application.Abstractions;

namespace TNLAStation.Infrastructure.Mirakurun;

public sealed partial class MirakurunClient(
    HttpClient httpClient,
    IOptions<MirakurunOptions> options,
    ILogger<MirakurunClient> logger) : IChannelLogoProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = false
    };
    private readonly MirakurunOptions options = options.Value;

    public async ValueTask<IReadOnlyList<MirakurunServiceDto>> GetServicesAsync(
        CancellationToken cancellationToken)
    {
        using CancellationTokenSource timeout = CreateRequestTimeout(cancellationToken);
        MirakurunServiceDto[]? result = await httpClient.GetFromJsonAsync<MirakurunServiceDto[]>(
            "api/services",
            JsonOptions,
            timeout.Token);
        return result ?? throw new InvalidDataException("Mirakurun returned an empty services document.");
    }

    public async ValueTask<IReadOnlyList<MirakurunProgramDto>> GetProgramsAsync(
        CancellationToken cancellationToken)
    {
        using CancellationTokenSource timeout = CreateRequestTimeout(cancellationToken);
        MirakurunProgramDto[]? result = await httpClient.GetFromJsonAsync<MirakurunProgramDto[]>(
            "api/programs",
            JsonOptions,
            timeout.Token);
        return result ?? throw new InvalidDataException("Mirakurun returned an empty programs document.");
    }

    public async IAsyncEnumerable<MirakurunEventDto> ReadEventsAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "api/events/stream");
        using HttpResponseMessage response = await httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();
        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);
        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            string trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed is "[" or "," or "]")
            {
                continue;
            }

            MirakurunEventDto? item = JsonSerializer.Deserialize<MirakurunEventDto>(trimmed, JsonOptions);
            if (item is null)
            {
                throw new InvalidDataException("Mirakurun event stream contained a null event.");
            }

            LogEventReceived(logger, item.Resource, item.Type, item.Time);
            yield return item;
        }

        throw new EndOfStreamException("Mirakurun event stream ended without a closing event.");
    }

    public async ValueTask<ReadOnlyMemory<byte>> GetLogoAsync(
        long channelId,
        CancellationToken cancellationToken)
    {
        using CancellationTokenSource timeout = CreateRequestTimeout(cancellationToken);
        using HttpResponseMessage response = await httpClient.GetAsync(
            $"api/services/{channelId}/logo",
            HttpCompletionOption.ResponseHeadersRead,
            timeout.Token);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsByteArrayAsync(timeout.Token);
    }

    [LoggerMessage(
        EventId = 1000,
        Level = LogLevel.Debug,
        Message = "Received Mirakurun {Resource}/{EventType} event at {EventTime}")]
    private static partial void LogEventReceived(
        ILogger logger,
        string resource,
        string eventType,
        long eventTime);

    private CancellationTokenSource CreateRequestTimeout(CancellationToken cancellationToken)
    {
        var source = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        source.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, options.RequestTimeoutSeconds)));
        return source;
    }
}
