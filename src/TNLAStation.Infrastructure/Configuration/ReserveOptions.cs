namespace TNLAStation.Infrastructure.Configuration;

public sealed class ReserveOptions
{
    public const string SectionName = "Reserve";

    /// <summary>
    /// 何日先の番組表まで予約に使うか。先まで見ても放送までに内容が変わって作り直しになる。
    /// </summary>
    public int HorizonDays { get; init; } = 8;

    /// <summary>
    /// 定期的に作り直す間隔。番組表の更新と、時間が過ぎた予約の掃除を兼ねる。
    /// </summary>
    public int UpdateIntervalMinutes { get; init; } = 10;

    /// <summary>
    /// ルール録画の重複回避のための記憶を、何日分残すか。録画本体を消してもこの期間内は
    /// 「録った」事実だけ覚えていて、同じ番組を録り直さない。
    /// </summary>
    public int RecordedHistoryRetentionPeriodDays { get; init; } = 90;

    /// <summary>
    /// 予約の定期更新で毎回出るログを抑えるか (EPGStation の isSuppressReservesUpdateAllLog 相当)。
    /// 更新間隔を短くすると頻繁に出て煩わしいので、必要な人だけ切れるようにする。
    /// </summary>
    public bool IsSuppressReservesUpdateAllLog { get; init; }
}
