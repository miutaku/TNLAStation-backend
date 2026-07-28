namespace TNLAStation.Application.Abstractions;

/// <summary>
/// 録画一覧が返す録画 ID と、実際に動いている録画セッションを結び付けるための情報。
/// 予約 ID は作り直すたびに変わり得るため、停止後も復活させない処理には安定した予約キーを使う。
/// </summary>
public sealed record RecordingJobIdentity(
    long RecordedId,
    long ReserveId,
    string ReserveKey,
    long? ManualReserveId);

/// <summary>
/// 録画中のセッションを、HTTP の停止要求から止めるための橋渡し。
/// </summary>
public interface IRecordingJobRegistry
{
    /// <summary>録画セッションを登録する。返り値を破棄すると登録が外れる。</summary>
    IDisposable Register(RecordingJobIdentity identity, CancellationTokenSource cancellation);

    /// <summary>停止前に、対応する予約を取り消すための識別情報を得る。</summary>
    RecordingJobIdentity? Find(long recordedId);

    /// <summary>
    /// 停止を要求する。返されたタスクは、受信ストリームを閉じて途中までの録画を確定し、
    /// セッションの登録を外したときに完了する。実行中でなければ null。
    /// </summary>
    Task? RequestStop(long recordedId);
}

/// <summary>
/// 録画 ID を指定して、録画セッションと元の予約を一緒に停止する。
/// </summary>
public interface IRecordingStopService
{
    /// <summary>録画中なら停止して true。録画中の項目でなければ false。</summary>
    ValueTask<bool> StopAsync(long recordedId, CancellationToken cancellationToken);
}
