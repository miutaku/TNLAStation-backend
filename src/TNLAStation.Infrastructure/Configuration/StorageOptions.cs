namespace TNLAStation.Infrastructure.Configuration;

public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    /// <summary>
    /// Recording destinations, in the order EPGStation lists them under <c>config.recorded</c>.
    /// The default stays empty because the configuration binder appends to, rather than replaces,
    /// a pre-populated collection, which would leave a phantom directory in every deployment.
    /// </summary>
    public IReadOnlyList<RecordedDirectoryOptions> RecordedDirectories { get; init; } = [];
}

public sealed class RecordedDirectoryOptions
{
    public string Name { get; init; } = string.Empty;

    public string Path { get; init; } = string.Empty;
}
