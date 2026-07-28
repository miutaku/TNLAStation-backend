namespace TNLAStation.FfmpegWorker.Options;

public sealed class FfmpegOptions
{
    public const string SectionName = "Ffmpeg";

    public string FfmpegPath { get; init; } = "ffmpeg";

    public string FfprobePath { get; init; } = "ffprobe";

    /// <summary>
    /// HLS のプレイリスト・セグメントの置き場。backend と同じボリュームを共有し、
    /// backend はここを読み取り専用でマウントして静的配信する。
    /// </summary>
    public string WorkDirectory { get; init; } = "/var/lib/tnlastation/streamfiles";

    /// <summary>
    /// エンコード・サムネイル抽出・probe・HLS/変換配信で同時に起動する ffmpeg/ffprobe
    /// プロセス数の上限 (EPGStation の encodeProcessNum 相当)。0 で無制限。
    /// </summary>
    public int EncodeProcessNum { get; init; }
}
