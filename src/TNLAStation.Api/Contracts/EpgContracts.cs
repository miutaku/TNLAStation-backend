using System.Text.Json.Serialization;

namespace TNLAStation.Api.Contracts;

public sealed record ChannelItemResponse(
    long Id,
    int ServiceId,
    int NetworkId,
    string Name,
    string HalfWidthName,
    bool HasLogoData,
    string ChannelType,
    string Channel)
{
    public int? RemoteControlKeyId { get; init; }

    public int? Type { get; init; }
}

public sealed record ScheduleChannelResponse(
    long Id,
    int ServiceId,
    int NetworkId,
    string Name,
    bool HasLogoData,
    string ChannelType)
{
    public int? RemoteControlKeyId { get; init; }

    public int? Type { get; init; }
}

public sealed record ScheduleProgramResponse(
    long Id,
    long ChannelId,
    long StartAt,
    long EndAt,
    bool IsFree,
    string Name)
{
    public string? Description { get; init; }

    public string? Extended { get; init; }

    public IReadOnlyDictionary<string, string>? RawExtended { get; init; }

    public int? Genre1 { get; init; }

    public int? SubGenre1 { get; init; }

    public int? Genre2 { get; init; }

    public int? SubGenre2 { get; init; }

    public int? Genre3 { get; init; }

    public int? SubGenre3 { get; init; }

    public string? VideoType { get; init; }

    public string? VideoResolution { get; init; }

    public int? VideoStreamContent { get; init; }

    public int? VideoComponentType { get; init; }

    public int? AudioSamplingRate { get; init; }

    public int? AudioComponentType { get; init; }
}

public sealed record ScheduleResponse(
    ScheduleChannelResponse Channel,
    IReadOnlyList<ScheduleProgramResponse> Programs);

public sealed class ScheduleSearchRequest
{
    [JsonRequired]
    public required RuleSearchRequest Option { get; init; }

    [JsonRequired]
    public bool IsHalfWidth { get; init; }

    public int? Limit { get; init; }
}

public sealed class RuleSearchRequest
{
    public string? Keyword { get; init; }

    public string? IgnoreKeyword { get; init; }

    public bool? KeyCS { get; init; }

    public bool? KeyRegExp { get; init; }

    public bool? Name { get; init; }

    public bool? Description { get; init; }

    public bool? Extended { get; init; }

    public bool? IgnoreKeyCS { get; init; }

    public bool? IgnoreKeyRegExp { get; init; }

    public bool? IgnoreName { get; init; }

    public bool? IgnoreDescription { get; init; }

    public bool? IgnoreExtended { get; init; }

    [JsonPropertyName("GR")]
    public bool? Gr { get; init; }

    [JsonPropertyName("BS")]
    public bool? Bs { get; init; }

    [JsonPropertyName("CS")]
    public bool? Cs { get; init; }

    [JsonPropertyName("SKY")]
    public bool? Sky { get; init; }

    public IReadOnlyList<long>? ChannelIds { get; init; }

    public IReadOnlyList<SearchGenreRequest>? Genres { get; init; }

    public IReadOnlyList<SearchTimeRequest>? Times { get; init; }

    public bool? IsFree { get; init; }

    public int? DurationMin { get; init; }

    public int? DurationMax { get; init; }

    public IReadOnlyList<SearchPeriodRequest>? SearchPeriods { get; init; }
}

public sealed class SearchGenreRequest
{
    [JsonRequired]
    public int Genre { get; init; }

    public int? SubGenre { get; init; }
}

public sealed class SearchTimeRequest
{
    public int? Start { get; init; }

    public int? Range { get; init; }

    [JsonRequired]
    public int Week { get; init; }
}

public sealed class SearchPeriodRequest
{
    [JsonRequired]
    public long StartAt { get; init; }

    [JsonRequired]
    public long EndAt { get; init; }
}
