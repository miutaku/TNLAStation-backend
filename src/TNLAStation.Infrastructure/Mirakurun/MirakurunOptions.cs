namespace TNLAStation.Infrastructure.Mirakurun;

public sealed class MirakurunOptions
{
    public const string SectionName = "Mirakurun";

    public string? BaseUrl { get; init; }

    public int RequestTimeoutSeconds { get; init; } = 600;

    public int EventQueueCapacity { get; init; } = 4096;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(BaseUrl);
}
