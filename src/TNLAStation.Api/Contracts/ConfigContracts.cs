using System.Text.Json.Serialization;

namespace TNLAStation.Api.Contracts;

public sealed record ConfigResponse(
    int SocketIOPort,
    BroadcastResponse Broadcast,
    IReadOnlyList<string> Recorded,
    IReadOnlyList<string> Encode,
    UrlSchemeResponse Urlscheme,
    bool IsEnableTSLiveStream,
    bool IsEnableTSRecordedStream,
    bool IsEnableEncodedRecordedStream)
{
    public StreamConfigurationResponse? StreamConfig { get; init; }

    public IReadOnlyList<string>? KodiHosts { get; init; }
}

public sealed record BroadcastResponse(
    [property: JsonPropertyName("GR")] bool Gr,
    [property: JsonPropertyName("BS")] bool Bs,
    [property: JsonPropertyName("CS")] bool Cs,
    [property: JsonPropertyName("SKY")] bool Sky);

public sealed record UrlSchemeResponse(
    [property: JsonPropertyName("m2ts")] UrlSchemeInfoResponse M2Ts,
    UrlSchemeInfoResponse Video,
    UrlSchemeInfoResponse Download);

public sealed record UrlSchemeInfoResponse
{
    public string? Ios { get; init; }

    public string? Android { get; init; }

    public string? Mac { get; init; }

    public string? Win { get; init; }
}

public sealed record StreamConfigurationResponse
{
    public LiveStreamConfigurationResponse? Live { get; init; }

    public RecordedStreamConfigurationResponse? Recorded { get; init; }
}

public sealed record LiveStreamConfigurationResponse
{
    public TransportStreamConfigurationResponse? Ts { get; init; }
}

public sealed record TransportStreamConfigurationResponse
{
    [JsonPropertyName("m2ts")]
    public IReadOnlyList<M2TsStreamParameterResponse>? M2Ts { get; init; }

    [JsonPropertyName("m2tsll")]
    public IReadOnlyList<string>? M2TsLl { get; init; }

    public IReadOnlyList<string>? Webm { get; init; }

    public IReadOnlyList<string>? Mp4 { get; init; }

    public IReadOnlyList<string>? Hls { get; init; }

    /// <summary>EPGStation に無い TNLAStation の追加 (docs/compatibility.md)。</summary>
    [JsonPropertyName("lowlatency")]
    public IReadOnlyList<string>? LowLatency { get; init; }
}

public sealed record M2TsStreamParameterResponse(string Name, bool IsUnconverted);

public sealed record RecordedStreamConfigurationResponse
{
    public RecordedStreamModesResponse? Ts { get; init; }

    public RecordedStreamModesResponse? Encoded { get; init; }
}

public sealed record RecordedStreamModesResponse
{
    public IReadOnlyList<string>? Webm { get; init; }

    public IReadOnlyList<string>? Mp4 { get; init; }

    public IReadOnlyList<string>? Hls { get; init; }
}
