namespace TNLAStation.Domain;

public sealed record EpgChannel(
    long Id,
    int ServiceId,
    int NetworkId,
    string Name,
    string HalfWidthName,
    int? RemoteControlKeyId,
    bool HasLogoData,
    int ChannelTypeId,
    string ChannelType,
    string Channel,
    int? ServiceType);

public sealed record EpgProgram(
    long Id,
    DateTimeOffset UpdateTime,
    long ChannelId,
    long EventId,
    int ServiceId,
    int NetworkId,
    DateTimeOffset StartAt,
    DateTimeOffset EndAt,
    int StartHour,
    int Week,
    long DurationMilliseconds,
    bool IsFree,
    string Name,
    string HalfWidthName,
    string ShortName,
    string ChannelType,
    string Channel,
    string? Description = null,
    string? HalfWidthDescription = null,
    string? Extended = null,
    string? HalfWidthExtended = null,
    IReadOnlyDictionary<string, string>? RawExtended = null,
    IReadOnlyDictionary<string, string>? RawHalfWidthExtended = null,
    int? Genre1 = null,
    int? SubGenre1 = null,
    int? Genre2 = null,
    int? SubGenre2 = null,
    int? Genre3 = null,
    int? SubGenre3 = null,
    string? VideoType = null,
    string? VideoResolution = null,
    int? VideoStreamContent = null,
    int? VideoComponentType = null,
    int? AudioSamplingRate = null,
    int? AudioComponentType = null,
    IReadOnlyList<long>? RelayProgramIds = null);

public sealed record EpgSnapshot(
    IReadOnlyList<EpgChannel> Channels,
    IReadOnlyList<EpgProgram> Programs,
    DateTimeOffset CapturedAt);

public sealed record EpgSyncState(
    long Generation,
    bool NeedsFullSync,
    DateTimeOffset? LastAttemptAt,
    DateTimeOffset? LastSuccessAt,
    DateTimeOffset? LastStreamEventAt,
    string? LastError);
