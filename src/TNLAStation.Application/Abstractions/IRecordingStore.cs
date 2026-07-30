using TNLAStation.Domain;

namespace TNLAStation.Application.Abstractions;

/// <summary>
/// 録画の開始を伝えるための一式。番組表から取れる内容は、放送が終われば番組表から消えるので、
/// 録画の側へ写して持つ。
/// </summary>
public sealed record RecordingStart(
    long? ProgramId,
    long? RuleId,
    long ChannelId,
    DateTimeOffset StartAt,
    DateTimeOffset EndAt,
    string Name,
    string HalfWidthName,
    string? Description = null,
    string? HalfWidthDescription = null,
    string? Extended = null,
    string? HalfWidthExtended = null,
    int? Genre1 = null,
    int? SubGenre1 = null,
    int? Genre2 = null,
    int? SubGenre2 = null,
    int? Genre3 = null,
    int? SubGenre3 = null,
    long? ReserveId = null,
    string? ReserveKey = null,
    long? ManualReserveId = null);

/// <summary>
/// 録り終わっていない録画。再起動したときに、書きかけのまま残った行を畳むのに使う。
/// </summary>
public sealed record UnfinishedRecording(
    long RecordedId,
    long VideoFileId,
    string ParentDirectoryName,
    string Filename);

/// <summary>
/// 録画の状態を残す口。ファイルの書き込みそのものは含まない。
/// </summary>
public interface IRecordingStore
{
    /// <summary>
    /// 録画を始めたことを残す。ファイルの行も同時に作る。完了時にしか残さないと、途中で
    /// 落ちたときに、どのファイルが書きかけだったのか分からなくなる。
    /// </summary>
    ValueTask<(long RecordedId, long VideoFileId)> BeginAsync(
        RecordingStart start,
        string parentDirectoryName,
        string filename,
        CancellationToken cancellationToken);

    /// <summary>EPG の延長・繰り上げを録画中のメタデータにも反映する。</summary>
    ValueTask UpdateEndAtAsync(long recordedId, DateTimeOffset endAt, CancellationToken cancellationToken);

    /// <summary>録り終わり。実際に書けた大きさを残す。</summary>
    ValueTask CompleteAsync(long recordedId, long videoFileId, long size, CancellationToken cancellationToken);

    /// <summary>
    /// 受信の取りこぼしを残す。数えた結果は録画そのものとは別で、無くても再生はできる。
    /// </summary>
    ValueTask SaveDropLogAsync(
        long recordedId,
        TransportStreamDefects defects,
        string parentDirectoryName,
        string filename,
        CancellationToken cancellationToken);

    /// <summary>1 バイトも書けなかった録画を無かったことにする。</summary>
    ValueTask AbortAsync(long recordedId, CancellationToken cancellationToken);

    /// <summary>この番組を既に録ったか。二重に録らないための確認。</summary>
    ValueTask<bool> ExistsAsync(long programId, CancellationToken cancellationToken);

    ValueTask<IReadOnlyList<UnfinishedRecording>> ListUnfinishedAsync(CancellationToken cancellationToken);
}

/// <summary>
/// 保存された動画ファイル 1 つ。保存先と、その中での相対パスに分けて持つ。保存先ごと
/// 移せるように、絶対パスでは持たない。
/// </summary>
public sealed record VideoFileLocation(
    long Id,
    long RecordedId,
    string Name,
    string ParentDirectoryName,
    string Filename,
    string Type,
    long Size)
{
    public string FullPath => Path.Combine(ParentDirectoryName, Filename);
}

public interface IVideoFileRepository
{
    ValueTask<VideoFileLocation?> GetAsync(long videoFileId, CancellationToken cancellationToken);

    /// <summary>
    /// ファイルを消す。録画に他のファイルが残っていれば録画そのものは残す。
    /// </summary>
    ValueTask<bool> DeleteAsync(long videoFileId, CancellationToken cancellationToken);
}

/// <summary>
/// 外で作った動画を録画へ結び付けるときの内容。
/// </summary>
public sealed record VideoFileUpload(
    long RecordedId,
    string Name,
    string OriginalFileName,
    string ParentDirectoryName,
    string? SubDirectory,
    string Type);

public interface IVideoFileUploadRepository
{
    /// <summary>結び付ける録画が無ければ null。</summary>
    ValueTask<long?> UploadAsync(
        VideoFileUpload upload,
        Stream content,
        CancellationToken cancellationToken);
}

/// <summary>
/// 取りこぼしの記録 1 件の置き場。
/// </summary>
public sealed record DropLogFileLocation(long Id, string ParentDirectoryName, string Filename)
{
    public string FullPath => Path.Combine(ParentDirectoryName, Filename);
}

public interface IDropLogRepository
{
    ValueTask<DropLogFileLocation?> GetAsync(long dropLogFileId, CancellationToken cancellationToken);
}
