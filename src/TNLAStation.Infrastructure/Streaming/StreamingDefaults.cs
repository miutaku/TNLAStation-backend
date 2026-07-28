using TNLAStation.Infrastructure.Configuration;

namespace TNLAStation.Infrastructure.Streaming;

/// <summary>
/// 設定が無いときに使う配信の既定値。<see cref="RemoteLiveStreamService"/> の実処理と、
/// <see cref="TNLAStation.Infrastructure.Repositories.MockConfigRepository"/> が
/// <c>/api/config</c> へ載せる選択肢の両方が、同じ既定値を指す必要がある — 片方だけ既定に
/// フォールバックすると、画面に出ない選択肢が実は使える (逆に出ているのに使えない) ことになる。
/// </summary>
internal static class StreamingDefaults
{
    /// <summary>設定が無いときの出力。mp4 は fragmented にする。</summary>
    public static readonly StreamFormatOptions[] Formats =
    [
        new()
        {
            Name = "m2ts",
            ContentType = "video/mp2t",
            Arguments = ["-c", "copy", "-f", "mpegts"],
        },
        new()
        {
            Name = "mp4",
            ContentType = "video/mp4",
            Arguments =
            [
                "-c:v", "libx264", "-preset", "veryfast", "-profile:v", "main",
                "-c:a", "aac",
                "-movflags", "frag_keyframe+empty_moov+default_base_moof",
                "-f", "mp4",
            ],
        },
        new()
        {
            Name = "webm",
            ContentType = "video/webm",
            Arguments =
            [
                "-c:v", "libvpx", "-deadline", "realtime", "-cpu-used", "8",
                "-c:a", "libopus",
                "-f", "webm",
            ],
        },
        new()
        {
            Name = "m2tsll",
            ContentType = "video/mp2t",
            Arguments =
            [
                "-c:v", "libx264", "-preset", "ultrafast", "-tune", "zerolatency", "-profile:v", "main",
                "-c:a", "aac",
                "-flush_packets", "1",
                "-f", "mpegts",
            ],
        },
    ];

    public static readonly LiveStreamModeOptions[] LiveModes =
    [
        new() { Name = "720p", Height = 720, VideoBitrate = "3000k", AudioBitrate = "192k" },
        new() { Name = "480p", Height = 480, VideoBitrate = "1500k", AudioBitrate = "128k" },
        new() { Name = "360p", Height = 360, VideoBitrate = "700k", AudioBitrate = "96k" },
    ];
}
