namespace TNLAStation.Infrastructure.Configuration;

/// <summary>
/// ffmpeg/ffprobe を実行する別コンテナ (TNLAStation.FfmpegWorker) の接続先。
/// backend 自身はもう ffmpeg を持たず、視聴・サムネイル・エンコードのすべてをここへ委ねる。
/// </summary>
public sealed class FfmpegWorkerOptions
{
    public const string SectionName = "FfmpegWorker";

    public string BaseUrl { get; init; } = string.Empty;
}
