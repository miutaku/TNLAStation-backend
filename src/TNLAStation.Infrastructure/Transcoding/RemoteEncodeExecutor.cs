using System.Net.Http.Json;
using System.Text.Json;
using TNLAStation.Application.Abstractions;

namespace TNLAStation.Infrastructure.Transcoding;

/// <summary>
/// エンコードの実行を ffmpeg-worker へ委ねる。進み具合は chunked な NDJSON で 1 行ずつ届く。
/// </summary>
public sealed class RemoteEncodeExecutor(HttpClient httpClient) : IEncodeExecutor
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<bool> RunAsync(
        string inputPath,
        string outputPath,
        IReadOnlyList<string> arguments,
        string? command,
        double? rateTimeoutMultiplier,
        IReadOnlyDictionary<string, string> environmentVariables,
        Func<int?, string?, CancellationToken, Task> onProgress,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "encode")
        {
            Content = JsonContent.Create(
                new EncodeRequest(inputPath, outputPath, arguments, command, rateTimeoutMultiplier, environmentVariables),
                options: JsonOptions),
        };
        using HttpResponseMessage response = await httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();

        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);
        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            if (line.Length == 0)
            {
                continue;
            }

            EncodeProgress? progress = JsonSerializer.Deserialize<EncodeProgress>(line, JsonOptions);
            if (progress is null)
            {
                continue;
            }

            if (progress.Done)
            {
                if (progress.Preempted)
                {
                    throw new EncodePreemptedException();
                }

                return progress.Succeeded;
            }

            await onProgress(progress.Percent, progress.Log, cancellationToken);
        }

        throw new InvalidOperationException("EncodeStreamEndedWithoutCompletion");
    }

    private sealed record EncodeRequest(
        string InputPath,
        string OutputPath,
        IReadOnlyList<string> Arguments,
        string? Command,
        double? RateTimeoutMultiplier,
        IReadOnlyDictionary<string, string> EnvironmentVariables);

    private sealed record EncodeProgress(bool Done, bool Succeeded, int? Percent, string? Log, bool Preempted = false);
}
