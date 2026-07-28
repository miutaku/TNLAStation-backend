namespace TNLAStation.Application.Abstractions;

/// <summary>
/// 録画済みファイル 1 本を ffmpeg で変換する実処理。EncodeWorker は待ち行列と DB の
/// 更新だけを持ち、実際に ffmpeg を回す場所はこの抽象の向こう側に任せる。
/// </summary>
public interface IEncodeExecutor
{
    /// <summary>
    /// 変換の間、進み具合とログの断片を <paramref name="onProgress"/> へ渡す。
    /// 戻り値は成功したかどうか。取り消しは例外として伝わる。
    /// </summary>
    /// <param name="command">
    /// 設定されていれば <paramref name="arguments"/> の代わりにこのコマンドをそのまま実行する
    /// (EPGStation の encode.cmd 相当)。
    /// </param>
    /// <param name="rateTimeoutMultiplier">
    /// 録画時間にこの倍率を掛けた時間を超えたら打ち切る (EPGStation の encode.rate 相当)。
    /// </param>
    /// <param name="environmentVariables">
    /// 実行するプロセスに渡す環境変数 (EPGStation が encode.cmd に渡す RECORDEDID/NAME/CHANNELID
    /// などの一式に相当)。<paramref name="command"/> の有無に関わらず渡す。
    /// </param>
    Task<bool> RunAsync(
        string inputPath,
        string outputPath,
        IReadOnlyList<string> arguments,
        string? command,
        double? rateTimeoutMultiplier,
        IReadOnlyDictionary<string, string> environmentVariables,
        Func<int?, string?, CancellationToken, Task> onProgress,
        CancellationToken cancellationToken);
}
