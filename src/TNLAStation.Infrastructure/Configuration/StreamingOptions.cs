using TNLAStation.Infrastructure.Configuration.EpgStation;

namespace TNLAStation.Infrastructure.Configuration;

public sealed class StreamingOptions
{
    public const string SectionName = "Streaming";

    /// <summary>
    /// EPGStation の <c>stream</c> をそのままの形で持つ。<c>/api/config</c> の
    /// <c>streamConfig</c> と <c>isEnableTS*Stream</c> はこの木の「有無」から決まるので、
    /// 名前の一覧へ潰さずに保持している。config.yml を読んでいる場合は
    /// <see cref="EpgStation.IEpgStationConfigAccessor"/> 側が同じ木を持つ。
    /// </summary>
    public EpgStationStreamConfig? Stream { get; init; }

    /// <summary>
    /// セグメントとプレイリストの置き場。実際に書き出すのは ffmpeg-worker (Ffmpeg:WorkDirectory) で、
    /// backend はここを読み取り専用で配信するだけ。同じボリュームを共有する必要があるため、
    /// 本番の Docker イメージでは <c>Streaming__WorkDirectory=/var/lib/tnlastation/streamfiles</c> を
    /// Dockerfile の ENV で worker 側と揃えて上書きする (Dockerfile 参照)。ここの既定値は
    /// Docker 無しでのローカル実行・テスト用に、root 権限が無くても書ける場所にしてある。
    /// </summary>
    public string WorkDirectory { get; init; } = "/var/tmp/tnlastation/streamfiles";

    /// <summary>
    /// keep が途切れてから配信を畳むまで。画面は 10 秒ごとに keep を送るので、
    /// 数回の取りこぼしは許容しつつ、閉じたタブがチューナーを掴み続けない長さにする。
    /// </summary>
    public int IdleTimeoutSeconds { get; init; } = 45;

    public int SegmentSeconds { get; init; } = 3;

    /// <summary>
    /// 最初のプレイリストを待つ長さ。地上波 1080p HLS の実測は 20 秒台。
    /// </summary>
    public int PlaylistTimeoutSeconds { get; init; } = 90;

    /// <summary>
    /// 同時に開ける配信の数の上限。0 (既定) なら ffmpeg-worker が自分の CPU から報告した
    /// 定員の合計を使う — worker が増えれば上限も増える。チューナー本数のように CPU 以外で
    /// 縛りたいときだけ明示する。
    /// </summary>
    public int MaxConcurrentStreams { get; init; }

    /// <summary>
    /// LL-HLS。外部の配信サーバー (MediaMTX) を置いた構成でだけ使える。
    /// </summary>
    public LowLatencyHlsOptions? LowLatencyHls { get; init; }

    /// <summary>
    /// null (未設定) ならコード内の既定を使う。EPGStation の stream.live.* と同じく、
    /// 空配列を明示すると「その配信方式を無効化する」意味になる — 未設定と空配列を区別する
    /// 必要があるため、既定値を空配列ではなく null にしてある。
    /// </summary>
    public IReadOnlyList<LiveStreamModeOptions>? LiveModes { get; init; }

    /// <summary>
    /// HLS 以外の出力。名前が URL の末尾になる。中身は設定で丸ごと差し替えられる。
    /// 機器ごとに再生できる形が違うので、こちらで決め打つ話ではない。null (未設定) ならコード内の
    /// 既定を使い、空配列を明示すると無効化になる (<see cref="LiveModes"/> と同じ理由)。
    /// </summary>
    public IReadOnlyList<StreamFormatOptions>? Formats { get; init; }
}

public sealed class LowLatencyHlsOptions
{
    /// <summary>
    /// 画面が取りに行くプレイリスト。<c>{streamId}</c> を差し替える。
    /// 空なら LL-HLS は無効で、選択肢にも出さない。
    /// </summary>
    public string? PlaylistUrlTemplate { get; init; }
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
