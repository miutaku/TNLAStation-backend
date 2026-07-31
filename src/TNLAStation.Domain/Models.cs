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
    IReadOnlyList<string>? KodiHosts = null,
    StreamConfiguration? StreamConfig = null);

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

/// <summary>
/// 画面が視聴の選択肢を組み立てるための一覧。EPGStation の config.streamConfig に相当し、
/// 実際に選べる形式・画質の名前だけを載せる (コマンドの中身は載せない)。
/// </summary>
public sealed record StreamConfiguration(
    LiveStreamConfiguration? Live,
    RecordedStreamConfiguration? Recorded);

public sealed record LiveStreamConfiguration(TransportStreamConfiguration? Ts);

public sealed record TransportStreamConfiguration(
    IReadOnlyList<M2TsStreamParameter>? M2Ts,
    IReadOnlyList<string>? M2TsLl,
    IReadOnlyList<string>? Webm,
    IReadOnlyList<string>? Mp4,
    IReadOnlyList<string>? Hls,
    IReadOnlyList<string>? LowLatency = null);

/// <summary>無変換配信は cmd を持たないので IsUnconverted。TNLAStation は常に無変換の 1 本だけを持つ。</summary>
public sealed record M2TsStreamParameter(string Name, bool IsUnconverted);

public sealed record RecordedStreamConfiguration(
    RecordedStreamModes? Ts,
    RecordedStreamModes? Encoded);

public sealed record RecordedStreamModes(
    IReadOnlyList<string>? Webm,
    IReadOnlyList<string>? Mp4,
    IReadOnlyList<string>? Hls);

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
    int Priority = 0,
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
    int? AudioComponentType = null,
    string? ReserveKey = null,
    long? ManualReserveId = null,
    string? RuleName = null);

/// <summary>
/// Disk usage of one recording destination, in bytes.
/// </summary>
public sealed record StorageUsage(
    string Name,
    long Available,
    long Used,
    long Total,
    IReadOnlyList<StorageFileUsage> FileTypes);

/// <summary>
/// Aggregate size and count for one kind of file below a recording destination.
/// Category and format are stable API identifiers; presentation labels belong to the client.
/// </summary>
public sealed record StorageFileUsage(
    string Category,
    string Format,
    long Count,
    long Size);

/// <summary>
/// エンコード待ち・実行中の 1 件。
/// </summary>
public sealed record EncodeQueueItem(
    long Id,
    string Mode,
    RecordedProgram Recorded,
    int? Percent = null,
    string? Log = null);

/// <summary>
/// 視聴・配信セッションの 1 件。
/// </summary>
public sealed record StreamSession(
    long StreamId,
    string Type,
    int Mode,
    bool IsEnable,
    long ChannelId,
    string Name,
    long? ProgramId = null,
    long? VideoFileId = null,
    long? StartAt = null,
    long? EndAt = null,
    string? Description = null,
    string? Extended = null);
