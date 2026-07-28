namespace TNLAStation.Infrastructure.Configuration;

public sealed class EpgOptions
{
    public const string SectionName = "Epg";

    public bool NeedToReplaceEnclosingCharacters { get; init; } = true;

    public int UpdateIntervalMinutes { get; init; } = 10;

    public IReadOnlyList<long> ChannelOrder { get; init; } = [];

    public IReadOnlyList<int> SidOrder { get; init; } = [];

    public IReadOnlyList<long> ExcludeChannels { get; init; } = [];

    public IReadOnlyList<int> ExcludeSids { get; init; } = [];
}
