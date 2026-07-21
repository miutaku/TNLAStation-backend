using TNLAStation.Domain;

namespace TNLAStation.Application.Models;

public sealed record RecordedQuery(
    bool IsHalfWidth,
    int Offset = 0,
    int Limit = 24,
    bool? IsReverse = null,
    long? RuleId = null,
    long? ChannelId = null,
    int? Genre = null,
    string? Keyword = null,
    bool? HasOriginalFile = null);

public sealed record CreateRecordedCommand(
    long ChannelId,
    long StartAt,
    long EndAt,
    string Name,
    long? RuleId = null,
    string? Description = null,
    string? Extended = null,
    int? Genre1 = null,
    int? SubGenre1 = null,
    int? Genre2 = null,
    int? SubGenre2 = null,
    int? Genre3 = null,
    int? SubGenre3 = null);

public sealed record ReserveQuery(
    bool IsHalfWidth,
    int Offset = 0,
    int Limit = 24,
    string? Type = null,
    long? RuleId = null);

public sealed record CreateReserveCommand(
    bool AllowEndLack,
    long? ProgramId,
    TimeSpecifiedReserve? TimeSpecified,
    IReadOnlyList<long>? Tags,
    ReserveSaveSettings? Save,
    ReserveEncodeSettings? Encode,
    int Priority = ReservePriority.Normal);

public sealed record TimeSpecifiedReserve(string Name, long ChannelId, long StartAt, long EndAt);

public sealed record ReserveSaveSettings(
    string? ParentDirectoryName,
    string? Directory,
    string? RecordedFormat);

public sealed record ReserveEncodeSettings(
    string? Mode1,
    string? EncodeParentDirectoryName1,
    string? Directory1,
    string? Mode2,
    string? EncodeParentDirectoryName2,
    string? Directory2,
    string? Mode3,
    string? EncodeParentDirectoryName3,
    string? Directory3,
    bool IsDeleteOriginalAfterEncode);

public sealed record Page<T>(IReadOnlyList<T> Items, int Total);

public sealed record RecordedTagQuery(int? Offset = null, int? Limit = null, string? Name = null);

public sealed record EncodeTasks(
    IReadOnlyList<EncodeQueueItem> Running,
    IReadOnlyList<EncodeQueueItem> Waiting);
