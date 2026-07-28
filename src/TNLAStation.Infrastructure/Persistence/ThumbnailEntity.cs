namespace TNLAStation.Infrastructure.Persistence;

/// <summary>
/// 録画のサムネイル。一覧で中身を思い出すためのもので、無くても録画は成立する。
/// </summary>
public sealed class ThumbnailEntity
{
    public long Id { get; set; }

    public long RecordedId { get; set; }

    public string ParentDirectoryName { get; set; } = string.Empty;

    public string Filename { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    public RecordedEntity? Recorded { get; set; }
}
