using System.Text.Json.Serialization;

namespace TNLAStation.Api.Contracts;

public sealed record ReservesResponse(IReadOnlyList<ReserveItemResponse> Reserves, int Total);

/// <summary>
/// 期間内の予約を状態ごとに分けた一覧。番組表の画面が、どの番組が予約済みかを引くのに使う。
/// </summary>
public sealed record ReserveListsResponse(
    IReadOnlyList<ReserveListItemResponse> Normal,
    IReadOnlyList<ReserveListItemResponse> Conflicts,
    IReadOnlyList<ReserveListItemResponse> Skips,
    IReadOnlyList<ReserveListItemResponse> Overlaps);

public sealed record ReserveListItemResponse(long ReserveId)
{
    public long? ProgramId { get; init; }

    public long? RuleId { get; init; }
}

public sealed record ReserveItemResponse(
    long Id,
    bool IsSkip,
    bool IsConflict,
    bool IsOverlap,
    bool AllowEndLack,
    bool IsTimeSpecified,
    bool IsDeleteOriginalAfterEncode,
    long ChannelId,
    long StartAt,
    long EndAt,
    string Name)
{
    public long? RuleId { get; init; }

    public int Priority { get; init; }

    public IReadOnlyList<long>? Tags { get; init; }

    public string? ParentDirectoryName { get; init; }

    public string? Directory { get; init; }

    public string? RecordedFormat { get; init; }

    public string? EncodeMode1 { get; init; }

    public string? EncodeParentDirectoryName1 { get; init; }

    public string? EncodeDirectory1 { get; init; }

    public string? EncodeMode2 { get; init; }

    public string? EncodeParentDirectoryName2 { get; init; }

    public string? EncodeDirectory2 { get; init; }

    public string? EncodeMode3 { get; init; }

    public string? EncodeParentDirectoryName3 { get; init; }

    public string? EncodeDirectory3 { get; init; }

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
}

public sealed class CreateReserveRequest
{
    [JsonRequired]
    public bool AllowEndLack { get; init; }

    public long? ProgramId { get; init; }

    public TimeSpecifiedOptionRequest? TimeSpecifiedOption { get; init; }

    public IReadOnlyList<long>? Tags { get; init; }

    public ReserveSaveOptionRequest? SaveOption { get; init; }

    public ReserveEncodeOptionRequest? EncodeOption { get; init; }

    /// <summary>チューナーが足りないときに、どれを先に取るか。大きいほうが先。</summary>
    public int Priority { get; init; }
}

public sealed class TimeSpecifiedOptionRequest
{
    [JsonRequired]
    public string Name { get; init; } = string.Empty;

    [JsonRequired]
    public long ChannelId { get; init; }

    [JsonRequired]
    public long StartAt { get; init; }

    [JsonRequired]
    public long EndAt { get; init; }
}

public sealed record ReserveSaveOptionRequest(
    string? ParentDirectoryName,
    string? Directory,
    string? RecordedFormat);

public sealed class ReserveEncodeOptionRequest
{
    public string? Mode1 { get; init; }

    public string? EncodeParentDirectoryName1 { get; init; }

    public string? Directory1 { get; init; }

    public string? Mode2 { get; init; }

    public string? EncodeParentDirectoryName2 { get; init; }

    public string? Directory2 { get; init; }

    public string? Mode3 { get; init; }

    public string? EncodeParentDirectoryName3 { get; init; }

    public string? Directory3 { get; init; }

    [JsonRequired]
    public bool IsDeleteOriginalAfterEncode { get; init; }
}

public sealed record AddedReserveResponse(long ReserveId);
