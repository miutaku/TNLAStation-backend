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
}
