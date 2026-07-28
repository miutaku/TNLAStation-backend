namespace TNLAStation.Infrastructure.Configuration;

public sealed class EncodeOptions
{
    public const string SectionName = "Encode";

    /// <summary>待ち行列を見に行く間隔。</summary>
    public int PollIntervalSeconds { get; init; } = 10;

    /// <summary>
    /// 同時に処理するエンコードの数 (EPGStation の <c>concurrentEncodeNum</c>)。既定値 0 は
    /// 「エンコード機能を使わない」という意味で、上流と同じく <c>POST /api/encode</c> は
    /// <c>CncurrentEncodeNumIsZero</c> (上流の綴りのまま) で 500 になる。
    /// 根拠: EPGStation/src/model/service/encode/EncodeManageModel.ts の push()。
    /// </summary>
    public int ConcurrentEncodeNum { get; init; }

    /// <summary>
    /// エンコードと配信を合わせた ffmpeg プロセスの上限 (EPGStation の <c>encodeProcessNum</c>)。
    /// 上限に達しているとき、より優先度の高い要求は既存の低い方を 1 つ止めて場所を空ける。
    /// 根拠: EPGStation/src/model/service/encode/EncodeProcessManageModel.ts の create()。
    /// </summary>
    public int ProcessNum { get; init; }

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
    /// 入力と出力の間に挟む ffmpeg の引数。<see cref="Command"/> を設定しなければこちらを使う。
    /// 画質の好みは人によって違うので、こちらで決め打つ話ではない。
    /// </summary>
    public IReadOnlyList<string> Arguments { get; init; } = [];

    /// <summary>
    /// 設定すると、<see cref="Arguments"/> の代わりにこのコマンドをそのまま実行する
    /// (EPGStation の encode.cmd 相当)。%INPUT%/%OUTPUT%/%FFMPEG%/%FFPROBE% を置換できる。
    /// ffmpeg に限らず任意の実行ファイルを指定できるが、進捗 (パーセント) は追わない。
    /// </summary>
    public string? Command { get; init; }

    /// <summary>
    /// 録画時間 (秒) にこの値を掛けた時間でタイムアウトする (EPGStation の encode.rate 相当)。
    /// </summary>
    public double RateTimeoutMultiplier { get; init; } = 4.0;
}
