namespace TNLAStation.Domain;

public sealed record StationConfiguration(
    int SocketIoPort,
    BroadcastAvailability Broadcast,
    IReadOnlyList<string> RecordedDirectories,
    IReadOnlyList<string> EncodeModes,
    UrlSchemeConfiguration UrlScheme,
    bool IsEnableTsLiveStream,
    bool IsEnableTsRecordedStream,
    bool IsEnableEncodedRecordedStream,
    IReadOnlyList<string>? KodiHosts = null);

public sealed record BroadcastAvailability(bool Gr, bool Bs, bool Cs, bool Sky);

public sealed record UrlSchemeConfiguration(
    UrlSchemeInfo M2Ts,
    UrlSchemeInfo Video,
    UrlSchemeInfo Download);

public sealed record UrlSchemeInfo(
    string? Ios = null,
    string? Android = null,
    string? Mac = null,
    string? Win = null);

public sealed record RecordedProgram(
    long Id,
    long ChannelId,
    long StartAt,
    long EndAt,
    string Name,
    string HalfWidthName,
    bool IsRecording,
    bool IsEncoding,
    bool IsProtected,
    long? RuleId = null,
    long? ProgramId = null,
    string? Description = null,
    string? HalfWidthDescription = null,
    string? Extended = null,
    string? HalfWidthExtended = null,
    IReadOnlyDictionary<string, string>? RawExtended = null,
    IReadOnlyDictionary<string, string>? HalfWidthRawExtended = null,
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
    IReadOnlyList<long>? Thumbnails = null,
    IReadOnlyList<VideoFile>? VideoFiles = null,
    DropLogFile? DropLogFile = null,
    IReadOnlyList<RecordedTag>? Tags = null);

public sealed record VideoFile(long Id, string Name, string Filename, string Type, long Size);

public sealed record DropLogFile(long Id, int ErrorCount, int DropCount, int ScramblingCount);

public sealed record RecordedTag(long Id, string Name, string Color);

public sealed record Reservation(
    long Id,
    bool IsSkip,
    bool IsConflict,
    bool IsOverlap,
    bool AllowEndLack,
    bool IsTimeSpecified,
    bool IsDeleteOriginalAfterEncode,
    long ChannelId,
    long StartAt,
    long EndAt,
    string Name,
    string HalfWidthName,
    long? RuleId = null,
    IReadOnlyList<long>? Tags = null,
    string? ParentDirectoryName = null,
    string? Directory = null,
    string? RecordedFormat = null,
    string? EncodeMode1 = null,
    string? EncodeParentDirectoryName1 = null,
    string? EncodeDirectory1 = null,
    string? EncodeMode2 = null,
    string? EncodeParentDirectoryName2 = null,
    string? EncodeDirectory2 = null,
    string? EncodeMode3 = null,
    string? EncodeParentDirectoryName3 = null,
    string? EncodeDirectory3 = null,
    long? ProgramId = null,
    string? Description = null,
    string? HalfWidthDescription = null,
    string? Extended = null,
    string? HalfWidthExtended = null,
    IReadOnlyDictionary<string, string>? RawExtended = null,
    IReadOnlyDictionary<string, string>? HalfWidthRawExtended = null,
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
    int? AudioComponentType = null);

/// <summary>
/// Disk usage of one recording destination, in bytes.
/// </summary>
public sealed record StorageUsage(string Name, long Available, long Used, long Total);
