namespace TNLAStation.Infrastructure.Configuration;

public sealed class ThumbnailOptions
{
    public const string SectionName = "Thumbnail";

    public string Directory { get; init; } = "/var/lib/tnlastation/thumbnails";

    public int Width { get; init; } = 480;

    /// <summary>長さが分からないときに取り出す位置。</summary>
    public double PositionSeconds { get; init; } = 30;
}
