namespace TNLAStation.Application.Abstractions;

public sealed record ThumbnailFile(long Id, long RecordedId, string ParentDirectoryName, string Filename)
{
    public string FullPath => Path.Combine(ParentDirectoryName, Filename);
}

/// <summary>
/// 録画のサムネイル。一覧で中身を思い出すためのもので、無くても録画は成立する。
/// 作れなくても録画そのものは失敗にしない。
/// </summary>
public interface IThumbnailService
{
    ValueTask<ThumbnailFile?> GetAsync(long thumbnailId, CancellationToken cancellationToken);

    /// <summary>
    /// 動画ファイルから 1 枚作る。既にあれば作り直さない。作れなければ null。
    /// </summary>
    ValueTask<long?> CreateForVideoFileAsync(long videoFileId, CancellationToken cancellationToken);

    /// <summary>まだ持っていない録画すべてに作る。</summary>
    ValueTask<int> CreateMissingAsync(CancellationToken cancellationToken);

    ValueTask<bool> DeleteAsync(long thumbnailId, CancellationToken cancellationToken);

    /// <summary>元の録画が消えた画像を片付ける。</summary>
    ValueTask<int> CleanupAsync(CancellationToken cancellationToken);
}
