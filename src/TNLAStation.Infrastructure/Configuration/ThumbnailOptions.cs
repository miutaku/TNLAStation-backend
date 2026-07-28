namespace TNLAStation.Infrastructure.Configuration;

public sealed class ThumbnailOptions
{
    public const string SectionName = "Thumbnail";

    public string Directory { get; init; } = "/var/lib/tnlastation/thumbnails";

    public int Width { get; init; } = 480;

    /// <summary>
    /// 高さ。EPGStation の thumbnailSize (幅x高さ) に合わせ、既定は 480x270 と同じ結果になる
    /// 270 にしている。アスペクト比を保ちたい場合は null を指定する (幅だけ合わせて自動計算)。
    /// </summary>
    public int? Height { get; init; } = 270;

    /// <summary>
    /// サムネイルを切り出す再生位置 (秒)。EPGStation の thumbnailPosition と同じく、
    /// 動画の長さに関わらず常にこの秒数を使う。
    /// </summary>
    public double PositionSeconds { get; init; } = 5;

    /// <summary>
    /// 設定すると、固定の ffmpeg 引数の代わりにこのコマンドをそのまま実行する
    /// (EPGStation の thumbnailCmd 相当)。%FFMPEG%/%INPUT%/%OUTPUT%/%THUMBNAIL_POSITION%/
    /// %THUMBNAIL_SIZE% を置換できる。
    /// </summary>
    public string? Command { get; init; }
}
