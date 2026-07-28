using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using TNLAStation.Application.Abstractions;

namespace TNLAStation.Infrastructure.Streaming;

/// <summary>
/// duration の取得を ffmpeg-worker へ委ねる。ffprobe は backend コンテナには無い。
/// </summary>
public sealed class RemoteMediaProbe(HttpClient httpClient) : IMediaProbe
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async ValueTask<double?> GetDurationSecondsAsync(string path, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await httpClient.PostAsJsonAsync(
            "probe",
            new ProbeRequest(path),
            JsonOptions,
            cancellationToken);
        response.EnsureSuccessStatusCode();
        ProbeResponse? result = await response.Content.ReadFromJsonAsync<ProbeResponse>(JsonOptions, cancellationToken);
        return result?.DurationSeconds;
    }

    private sealed record ProbeRequest(string Path);

    private sealed record ProbeResponse([property: JsonPropertyName("durationSeconds")] double? DurationSeconds);
}
