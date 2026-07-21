namespace TNLAStation.Infrastructure.Configuration;

public sealed class StreamingOptions
{
    public const string SectionName = "Streaming";

    public string FfmpegPath { get; init; } = "ffmpeg";

    public string FfprobePath { get; init; } = "ffprobe";

    /// <summary>
    /// セグメントとプレイリストの置き場。再生が終われば消える一時ファイルなので、
    /// 録画の保存先とは分ける。
    /// </summary>
    public string WorkDirectory { get; init; } = "/var/tmp/tnlastation/streamfiles";

    /// <summary>
    /// keep が途切れてから配信を畳むまで。画面は 10 秒ごとに keep を送るので、
    /// 数回の取りこぼしは許容しつつ、閉じたタブがチューナーを掴み続けない長さにする。
    /// </summary>
    public int IdleTimeoutSeconds { get; init; } = 45;

    public int SegmentSeconds { get; init; } = 3;

    /// <summary>
    /// 同時に開ける配信の数。チューナーの本数を超えて開いても Mirakurun 側で失敗するだけなので、
    /// ここで止めて理由の分かるエラーにする。
    /// </summary>
    public int MaxConcurrentStreams { get; init; } = 2;

    public IReadOnlyList<LiveStreamModeOptions> LiveModes { get; init; } = [];

    /// <summary>
    /// HLS 以外の出力。名前が URL の末尾になる。中身は設定で丸ごと差し替えられる。
    /// 機器ごとに再生できる形が違うので、こちらで決め打つ話ではない。
    /// </summary>
    public IReadOnlyList<StreamFormatOptions> Formats { get; init; } = [];
}

/// <summary>
/// 画質。EPGStation の <c>stream.live.hls</c> と同じく、添字がそのまま mode になる。
/// </summary>
public sealed class LiveStreamModeOptions
{
    public string Name { get; init; } = string.Empty;

    public int Height { get; init; }

    public string VideoBitrate { get; init; } = string.Empty;

    public string AudioBitrate { get; init; } = string.Empty;
}

public sealed class StreamFormatOptions
{
    public string Name { get; init; } = string.Empty;

    public string ContentType { get; init; } = "video/mp4";

    public IReadOnlyList<string> Arguments { get; init; } = [];
}
