namespace TNLAStation.Api.Contracts;

/// <summary>
/// 中身が空になりうる一覧の応答。録画していない、エンコード待ちがない、tag を作っていない、
/// といった状態は正常なので、404 ではなく空の配列を返す。
/// </summary>
public sealed record EncodeInfoResponse(
    IReadOnlyList<EncodeProgramItemResponse> RunningItems,
    IReadOnlyList<EncodeProgramItemResponse> WaitItems);

public sealed record EncodeProgramItemResponse(long Id, string Mode, RecordedItemResponse Recorded)
{
    public int? Percent { get; init; }

    public string? Log { get; init; }
}

public sealed record StreamInfoResponse(IReadOnlyList<StreamInfoItemResponse> Items);

public sealed record StartStreamResponse(long StreamId);

public sealed record StreamInfoItemResponse(
    long StreamId,
    string Type,
    int Mode,
    bool IsEnable,
    long ChannelId,
    string Name)
{
    public long? ProgramId { get; init; }

    public long? VideoFileId { get; init; }

    public long? StartAt { get; init; }

    public long? EndAt { get; init; }

    public string? Description { get; init; }

    public string? Extended { get; init; }
}

public sealed record RecordedTagsResponse(IReadOnlyList<RecordedTagResponse> Tags, int Total);

public sealed record ReserveCountsResponse(int Normal, int Conflicts, int Skips, int Overlaps);

public sealed record RecordedSearchOptionsResponse(
    IReadOnlyList<RecordedChannelListItemResponse> Channels,
    IReadOnlyList<RecordedGenreListItemResponse> Genres);

public sealed record RecordedChannelListItemResponse(int Cnt, long ChannelId);

public sealed record RecordedGenreListItemResponse(int Cnt, int Genre);
