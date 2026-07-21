namespace TNLAStation.Infrastructure.Configuration;

public sealed class RecordingOptions
{
    public const string SectionName = "Recording";

    /// <summary>
    /// 開始の何秒前から録り始めるか。放送は表の時刻ちょうどには始まらないので、少し前から録る。
    /// </summary>
    public int StartMarginSeconds { get; init; } = 10;

    /// <summary>
    /// 終了の何秒後まで録り続けるか。番組は押すほうが多い。
    /// </summary>
    public int EndMarginSeconds { get; init; } = 15;

    /// <summary>
    /// 予約表を見に行く間隔。開始時刻の判定はこの粒度になるので、開始マージンより短くする。
    /// </summary>
    public int PollIntervalSeconds { get; init; } = 5;

    /// <summary>
    /// 保存先。指定が無ければ Storage の最初の保存先を使う。
    /// </summary>
    public string? Directory { get; init; }
}
