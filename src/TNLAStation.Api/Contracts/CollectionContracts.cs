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

public sealed record AddedRecordedTagResponse(long TagId);

public sealed class RecordedTagRequest
{
    public string Name { get; init; } = string.Empty;

    /// <summary>一覧で見分けるための色。指定が無ければ既定の色にする。</summary>
    public string Color { get; init; } = "#4caf50";
}

public sealed class RelateRecordedTagRequest
{
    public long RecordedId { get; init; }
}

public sealed record VideoDurationResponse(double Duration);

public sealed record AddedEncodeResponse(long EncodeId);

public sealed class AddEncodeRequest
{
    public long RecordedId { get; init; }

    public long SourceVideoFileId { get; init; }

    public string Mode { get; init; } = string.Empty;

    public bool RemoveOriginal { get; init; }

    /// <summary>保存先。isSaveSameDirectory が真なら見ない。</summary>
    public string? ParentDir { get; init; }

    public string? Directory { get; init; }

    public bool? IsSaveSameDirectory { get; init; }
}

public sealed record AddedThumbnailResponse(long ThumbnailId);


public sealed record UploadedVideoFileResponse(long VideoFileId);

public sealed class SendVideoLinkToKodiRequest
{
    public string KodiName { get; init; } = string.Empty;
}
