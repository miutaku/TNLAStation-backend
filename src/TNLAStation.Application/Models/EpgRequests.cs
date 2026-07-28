namespace TNLAStation.Application.Models;

public sealed record EpgScheduleQuery(
    DateTimeOffset StartAt,
    DateTimeOffset EndAt,
    IReadOnlyList<string>? ChannelTypes = null,
    long? ChannelId = null,
    bool? IsFree = null);

public sealed record EpgSearchGenre(int Genre, int? SubGenre = null);

public sealed record EpgSearchTime(int Week, int? Start = null, int? Range = null);

public sealed record EpgSearchPeriod(DateTimeOffset StartAt, DateTimeOffset EndAt);

public sealed record EpgSearchQuery(
    string? Keyword = null,
    string? IgnoreKeyword = null,
    bool KeyCaseSensitive = false,
    bool KeyRegularExpression = false,
    bool Name = false,
    bool Description = false,
    bool Extended = false,
    bool IgnoreKeyCaseSensitive = false,
    bool IgnoreKeyRegularExpression = false,
    bool IgnoreName = false,
    bool IgnoreDescription = false,
    bool IgnoreExtended = false,
    bool Gr = false,
    bool Bs = false,
    bool Cs = false,
    bool Sky = false,
    IReadOnlyList<long>? ChannelIds = null,
    IReadOnlyList<EpgSearchGenre>? Genres = null,
    IReadOnlyList<EpgSearchTime>? Times = null,
    bool IsFree = false,
    int? DurationMin = null,
    int? DurationMax = null,
    IReadOnlyList<EpgSearchPeriod>? SearchPeriods = null,
    int? Limit = null);
