namespace TNLAStation.Infrastructure.Persistence;

/// <summary>
/// 人が入れた予約。ルール予約と違い、番組表を引き直しても消えないので、入力そのものを残す。
/// </summary>
public sealed class ManualReserveEntity
{
    public long Id { get; set; }

    /// <summary>番組表から選んだ予約の番組。時刻指定ならない。</summary>
    public long? ProgramId { get; set; }

    public bool IsTimeSpecified { get; set; }

    public long ChannelId { get; set; }

    public string ChannelType { get; set; } = string.Empty;

    public DateTimeOffset StartAt { get; set; }

    public DateTimeOffset EndAt { get; set; }

    public string Name { get; set; } = string.Empty;

    public string HalfWidthName { get; set; } = string.Empty;

    public bool AllowEndLack { get; set; }

    public bool IsDeleteOriginalAfterEncode { get; set; }

    public string? TagsJson { get; set; }

    public string? ParentDirectoryName { get; set; }

    public string? Directory { get; set; }

    public string? RecordedFormat { get; set; }

    public string? Mode1 { get; set; }

    public string? ParentDirectoryName1 { get; set; }

    public string? Directory1 { get; set; }

    public string? Mode2 { get; set; }

    public string? ParentDirectoryName2 { get; set; }

    public string? Directory2 { get; set; }

    public string? Mode3 { get; set; }

    public string? ParentDirectoryName3 { get; set; }

    public string? Directory3 { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>
/// 生成された予約。番組表とルールから毎回作り直すので、人の入力はここに置かない。
/// 番組の詳細も持たない。番組表と結合して読むほうが、番組の変更に追従できる。
/// </summary>
public sealed class ReserveEntity
{
    public long Id { get; set; }

    /// <summary>作り直しても変わらない鍵。skip の指定はこの鍵に紐づく。</summary>
    public string Key { get; set; } = string.Empty;

    public string Source { get; set; } = string.Empty;

    public long? RuleId { get; set; }

    public long? ProgramId { get; set; }

    public long? ManualReserveId { get; set; }

    public long ChannelId { get; set; }

    public string ChannelType { get; set; } = string.Empty;

    public DateTimeOffset StartAt { get; set; }

    public DateTimeOffset EndAt { get; set; }

    public string Name { get; set; } = string.Empty;

    public string HalfWidthName { get; set; } = string.Empty;

    public bool IsSkip { get; set; }

    public bool IsConflict { get; set; }

    public bool IsOverlap { get; set; }

    /// <summary>割り当てたチューナー。録れない予約は持たない。</summary>
    public int? TunerIndex { get; set; }

    public DateTimeOffset GeneratedAt { get; set; }

    public ManualReserveEntity? ManualReserve { get; set; }
}

/// <summary>
/// 「この予約は録らない」という指定。予約の行ではなく鍵に紐づける。予約は番組表の更新で
/// 作り直されるので、行に持たせると人の意思がそのたびに消える。
/// </summary>
public sealed class ReserveSkipEntity
{
    public string Key { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }
}
