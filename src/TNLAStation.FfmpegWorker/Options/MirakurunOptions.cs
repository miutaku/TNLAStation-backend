namespace TNLAStation.FfmpegWorker.Options;

/// <summary>
/// ライブ視聴・ライブ配信は worker が直接 Mirakurun から MPEG-TS を取りに行く。
/// backend を経由させて二重に転送しない。
/// </summary>
public sealed class MirakurunOptions
{
    public const string SectionName = "Mirakurun";

    public string BaseUrl { get; init; } = string.Empty;

    public int RequestTimeoutSeconds { get; init; } = 30;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(BaseUrl);
}
