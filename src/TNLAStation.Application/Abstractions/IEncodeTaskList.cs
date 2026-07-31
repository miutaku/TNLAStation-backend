namespace TNLAStation.Application.Abstractions;

/// <summary>
/// エンコードを 1 件頼むときの内容。
/// </summary>
public sealed record EncodeRequest(
    long RecordedId,
    long SourceVideoFileId,
    string Mode,
    bool RemoveOriginal,
    string? ParentDirectoryName = null,
    string? Directory = null,
    bool IsSaveSameDirectory = false);

/// <summary>
/// 予約に付いていたエンコード設定 1 つ分。録画が終わってから待ち行列へ積む。
/// </summary>
public sealed record ReserveEncodeOption(
    string Mode,
    string? ParentDirectoryName,
    string? Directory);

/// <summary>
/// 1 本の録画に積むエンコード一式。録画中に編集できるので、終わる直前の値を使う。
/// </summary>
public sealed record ReserveEncodePlan(
    IReadOnlyList<ReserveEncodeOption> Options,
    bool RemoveOriginal)
{
    public static readonly ReserveEncodePlan None = new([], false);
}

/// <summary>
/// 実行待ち・実行中の 1 件。
/// </summary>
public sealed record EncodeTask(
    long Id,
    long RecordedId,
    long SourceVideoFileId,
    string Mode,
    bool RemoveOriginal,
    string? ParentDirectoryName,
    string? Directory,
    bool IsRunning,
    int? Percent,
    string? Log = null);

/// <summary>
/// エンコードの待ち行列。実際に変換するかどうかとは切り離す。頼んだことと、それが
/// 走ったかどうかは別の話なので。
/// </summary>
public interface IEncodeTaskList
{
    ValueTask<long> EnqueueAsync(EncodeRequest request, CancellationToken cancellationToken);

    ValueTask<IReadOnlyList<EncodeTask>> ListAsync(CancellationToken cancellationToken);

    /// <summary>取り消す。走っている最中でも受け付ける。</summary>
    ValueTask<bool> CancelAsync(long encodeId, CancellationToken cancellationToken);

    /// <summary>ある録画に紐づくものをまとめて取り消す。</summary>
    ValueTask<int> CancelForRecordedAsync(long recordedId, CancellationToken cancellationToken);
}
