namespace TNLAStation.Infrastructure.Persistence;

/// <summary>
/// ルール録画の重複回避のための記憶。<see cref="RecordedEntity"/> への外部キーを持たない —
/// 録画本体や行が消えたあとも、設定した保持期間の間はここに残って重複判定に使われる。
/// </summary>
public sealed class RecordedHistoryEntity
{
    public long Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public long ChannelId { get; set; }

    public DateTimeOffset EndAt { get; set; }
}
