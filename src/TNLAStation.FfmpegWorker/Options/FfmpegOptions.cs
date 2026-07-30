namespace TNLAStation.FfmpegWorker.Options;

public sealed class FfmpegOptions
{
    public const string SectionName = "Ffmpeg";

    public string FfmpegPath { get; set; } = "ffmpeg";

    public string FfprobePath { get; set; } = "ffprobe";

    /// <summary>
    /// HLS のプレイリスト・セグメントの置き場。backend と同じボリュームを共有し、
    /// backend はここを読み取り専用でマウントして静的配信する。
    /// </summary>
    public string WorkDirectory { get; init; } = "/var/lib/tnlastation/streamfiles";

    /// <summary>
    /// backendがHLS sessionのstatus・停止要求を同じPodへ返すための到達可能なURL。
    /// 複数workerで配信するときだけ指定し、単一workerでは空のままでよい。
    /// </summary>
    public string? PublicBaseUrl { get; init; }

    /// <summary>
    /// エンコード・サムネイル抽出・probe・HLS/変換配信で同時に起動する ffmpeg/ffprobe
    /// プロセス数の上限 (EPGStation の encodeProcessNum 相当)。0 で無制限。
    /// </summary>
    public int EncodeProcessNum { get; init; }
}
