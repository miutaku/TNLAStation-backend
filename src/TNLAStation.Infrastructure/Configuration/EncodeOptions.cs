namespace TNLAStation.Infrastructure.Configuration;

public sealed class EncodeOptions
{
    public const string SectionName = "Encode";

    /// <summary>待ち行列を見に行く間隔。</summary>
    public int PollIntervalSeconds { get; init; } = 10;

    /// <summary>
    /// 変換の設定。名前がそのまま config の encode に並び、画面の選択肢になる。
    /// </summary>
    public IReadOnlyList<EncodeModeOptions> Modes { get; init; } = [];
}

public sealed class EncodeModeOptions
{
    public string Name { get; init; } = string.Empty;

    public string Extension { get; init; } = ".mp4";

    /// <summary>
    /// 入力と出力の間に挟む ffmpeg の引数。設定で丸ごと差し替えられるようにする。
    /// 画質の好みは人によって違うので、こちらで決め打つ話ではない。
    /// </summary>
    public IReadOnlyList<string> Arguments { get; init; } = [];
}
