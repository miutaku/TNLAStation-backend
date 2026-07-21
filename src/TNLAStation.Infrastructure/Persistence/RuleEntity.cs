namespace TNLAStation.Infrastructure.Persistence;

/// <summary>
/// Flat storage for a recording rule. The list-shaped search options are kept as JSON documents
/// because they are read back whole, while the keyword columns stay flat so that rule lookup can
/// use an index. Half-width columns mirror EPGStation, which searches on the normalized text.
/// </summary>
public sealed class RuleEntity
{
    public long Id { get; set; }

    public long UpdateCount { get; set; }

    public bool IsTimeSpecification { get; set; }

    /// <summary>チューナーが足りないときに、どれを先に取るか。大きいほうが先。</summary>
    public int Priority { get; set; }

    public string? Keyword { get; set; }

    public string? HalfWidthKeyword { get; set; }

    public string? IgnoreKeyword { get; set; }

    public string? HalfWidthIgnoreKeyword { get; set; }

    public bool KeyCaseSensitive { get; set; }

    public bool KeyRegularExpression { get; set; }

    public bool Name { get; set; }

    public bool Description { get; set; }

    public bool Extended { get; set; }

    public bool IgnoreKeyCaseSensitive { get; set; }

    public bool IgnoreKeyRegularExpression { get; set; }

    public bool IgnoreName { get; set; }

    public bool IgnoreDescription { get; set; }

    public bool IgnoreExtended { get; set; }

    public bool Gr { get; set; }

    public bool Bs { get; set; }

    public bool Cs { get; set; }

    public bool Sky { get; set; }

    public string? ChannelIdsJson { get; set; }

    public string? GenresJson { get; set; }

    public string? TimesJson { get; set; }

    public bool IsFree { get; set; }

    public int? DurationMin { get; set; }

    public int? DurationMax { get; set; }

    public string? SearchPeriodsJson { get; set; }

    public bool Enable { get; set; }

    public bool AvoidDuplicate { get; set; }

    public int? PeriodToAvoidDuplicate { get; set; }

    public bool AllowEndLack { get; set; } = true;

    public string? TagsJson { get; set; }

    public string? ParentDirectoryName { get; set; }

    public string? Directory { get; set; }

    public string? RecordedFormat { get; set; }

    public string? Mode1 { get; set; }

    public string? ParentDirectoryName1 { get; set; }

    public string? Directory1 { get; set; }

    public string? Mode2 { get; set; }

    public string? ParentDirectoryName2 { get; set; }

    public string? Directory2 { get; set; }

    public string? Mode3 { get; set; }

    public string? ParentDirectoryName3 { get; set; }

    public string? Directory3 { get; set; }

    public bool IsDeleteOriginalAfterEncode { get; set; }
}
