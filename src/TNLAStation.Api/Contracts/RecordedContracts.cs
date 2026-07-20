using System.Text.Json.Serialization;

namespace TNLAStation.Api.Contracts;

public sealed record RecordsResponse(IReadOnlyList<RecordedItemResponse> Records, int Total);

public sealed record RecordedItemResponse(
    long Id,
    long ChannelId,
    long StartAt,
    long EndAt,
    string Name,
    bool IsRecording,
    bool IsEncoding,
    bool IsProtected)
{
    public long? RuleId { get; init; }

    public long? ProgramId { get; init; }

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

    public IReadOnlyList<long>? Thumbnails { get; init; }

    public IReadOnlyList<VideoFileResponse>? VideoFiles { get; init; }

    public DropLogFileResponse? DropLogFile { get; init; }

    public IReadOnlyList<RecordedTagResponse>? Tags { get; init; }
}

public sealed record VideoFileResponse(long Id, string Name, string Filename, string Type, long Size);

public sealed record DropLogFileResponse(long Id, int ErrorCnt, int DropCnt, int ScramblingCnt);

public sealed record RecordedTagResponse(long Id, string Name, string Color);

public sealed class CreateRecordedRequest
{
    [JsonRequired]
    public long ChannelId { get; init; }

    [JsonRequired]
    public long StartAt { get; init; }

    [JsonRequired]
    public long EndAt { get; init; }

    [JsonRequired]
    public string Name { get; init; } = string.Empty;

    public long? RuleId { get; init; }

    public string? Description { get; init; }

    public string? Extended { get; init; }

    public int? Genre1 { get; init; }

    public int? SubGenre1 { get; init; }

    public int? Genre2 { get; init; }

    public int? SubGenre2 { get; init; }

    public int? Genre3 { get; init; }

    public int? SubGenre3 { get; init; }
}

public sealed record CreatedRecordedResponse(long RecordedId);
