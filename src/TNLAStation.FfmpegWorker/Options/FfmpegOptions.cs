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
    /// LL-HLS のとき TS を送り込む先。<c>{streamId}</c> を差し替える。
    /// 空なら LL-HLS は無効。
    /// </summary>
    public string? LowLatencyPublishUrlTemplate { get; init; }

    /// <summary>
    /// エンコード・サムネイル抽出・probe・HLS/変換配信で同時に起動する ffmpeg/ffprobe
    /// プロセス数の上限 (EPGStation の encodeProcessNum 相当)。0 なら
    /// <see cref="StreamCpuCost"/> と割り当て CPU から決める。
    /// </summary>
    public int EncodeProcessNum { get; init; }

    /// <summary>
    /// ライブ 1 本の変換が要る CPU コア数。実測 (地上波 1080i → 720p) は 1.1〜1.2。
    /// 同時本数の上限はこの値で割って決まるので、下げすぎると全部が実時間に追いつかなくなる。
    /// </summary>
    public double StreamCpuCost { get; init; } = 1.2;

    /// <summary>
    /// backend から観測されない HLS セッションを自主的に畳むまでの秒数。backend は生きた
    /// セッションを 10 秒ごとに見に来るので、これを大きく超えて観測が無いのは backend が
    /// セッションを忘れた合図。backend の一時的な不調で誤って畳まないよう、余裕を持たせる。
    /// </summary>
    public int SessionUnobservedTimeoutSeconds { get; init; } = 120;
}
