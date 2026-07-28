namespace TNLAStation.Infrastructure.Mirakurun;

public sealed class MirakurunOptions
{
    public const string SectionName = "Mirakurun";

    public string? BaseUrl { get; init; }

    public int RequestTimeoutSeconds { get; init; } = 600;

    public int EventQueueCapacity { get; init; } = 4096;

    /// <summary>
    /// 録画時に Mirakurun へ渡すチューナー優先度。他プロセスと取り合いになったとき、
    /// 数字が大きいほど優先される (Mirakurun 自身の仕様)。EPGStation の既定値に合わせる。
    /// </summary>
    public int RecPriority { get; init; } = 2;

    /// <summary>
    /// 予約生成の時点でチューナーが足りず <c>IsConflict</c> になった録画に使う優先度。
    /// 通常の <see cref="RecPriority"/> と分けているのは、それでもなお他の視聴・録画より
    /// 優先させたい/させたくない、という運用判断を分離できるようにするため。
    /// </summary>
    public int ConflictPriority { get; init; } = 1;

    /// <summary>
    /// ライブ視聴 (HLS・変換配信・無変換配信) で Mirakurun へ渡すチューナー優先度。
    /// 録画の <see cref="RecPriority"/>/<see cref="ConflictPriority"/> とは別枠。
    /// </summary>
    public int StreamingPriority { get; init; }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(BaseUrl);
}
