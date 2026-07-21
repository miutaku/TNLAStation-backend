namespace TNLAStation.Infrastructure.Persistence;

/// <summary>
/// 録画 1 件。番組表から作られるが、番組表と違って消えない。放送が終わって番組表から
/// 番組が消えても、録ったものは残る。だから番組の内容はここへ写して持つ。
/// </summary>
public sealed class RecordedEntity
{
    public long Id { get; set; }

    public long? ProgramId { get; set; }

    public long? RuleId { get; set; }

    public long ChannelId { get; set; }

    public DateTimeOffset StartAt { get; set; }

    public DateTimeOffset EndAt { get; set; }

    public string Name { get; set; } = string.Empty;

    public string HalfWidthName { get; set; } = string.Empty;

    public string? Description { get; set; }

    public string? HalfWidthDescription { get; set; }

    public string? Extended { get; set; }

    public string? HalfWidthExtended { get; set; }

    public int? Genre1 { get; set; }

    public int? SubGenre1 { get; set; }

    public int? Genre2 { get; set; }

    public int? SubGenre2 { get; set; }

    public int? Genre3 { get; set; }

    public int? SubGenre3 { get; set; }

    /// <summary>いま録画している最中。落ちたまま残った行は起動時に畳む。</summary>
    public bool IsRecording { get; set; }

    /// <summary>消さない印。自動削除の対象から外す。</summary>
    public bool IsProtected { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public ICollection<VideoFileEntity> VideoFiles { get; } = [];

    public ICollection<RecordedTagLinkEntity> TagLinks { get; } = [];

    public ICollection<ThumbnailEntity> Thumbnails { get; } = [];
}

/// <summary>
/// 録画の実体ファイル。1 つの録画に、元の TS とエンコード後が並ぶことがある。
/// </summary>
public sealed class VideoFileEntity
{
    public long Id { get; set; }

    public long RecordedId { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>保存先の中での相対パス。保存先ごと移せるように、絶対パスでは持たない。</summary>
    public string Filename { get; set; } = string.Empty;

    public string ParentDirectoryName { get; set; } = string.Empty;

    /// <summary>ts か encoded。元のまま保存したか、変換したか。</summary>
    public string Type { get; set; } = string.Empty;

    public long Size { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public RecordedEntity? Recorded { get; set; }
}

public sealed class RecordedTagEntity
{
    public long Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Color { get; set; } = string.Empty;

    public ICollection<RecordedTagLinkEntity> Links { get; } = [];
}

public sealed class RecordedTagLinkEntity
{
    public long RecordedId { get; set; }

    public long TagId { get; set; }

    public RecordedEntity? Recorded { get; set; }

    public RecordedTagEntity? Tag { get; set; }
}
