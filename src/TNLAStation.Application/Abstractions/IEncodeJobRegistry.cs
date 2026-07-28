namespace TNLAStation.Application.Abstractions;

/// <summary>
/// 実行中のエンコードを取り消せるようにするための橋渡し。ワーカーが走らせている ffmpeg を
/// 待ち行列側 (取り消し要求) から止められるよう、タスク ID と停止手段を結び付けておく。
/// </summary>
public interface IEncodeJobRegistry
{
    /// <summary>実行を始めたタスクを登録する。返り値を破棄すると登録が外れる。</summary>
    IDisposable Register(long taskId, CancellationTokenSource cancellation);

    /// <summary>
    /// 指定のタスクが実行中なら停止を要求する。返されたタスクは、ワーカーがプロセスと
    /// 出力ファイルの後始末を終えて登録を外したときに完了する。実行中でなければ null。
    /// </summary>
    Task? RequestCancel(long taskId);
}
