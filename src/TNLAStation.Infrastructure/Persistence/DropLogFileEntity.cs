namespace TNLAStation.Infrastructure.Persistence;

/// <summary>
/// 録画 1 本ぶんの受信の質。再生して初めて音が飛ぶことに気づくのでは遅いので、
/// 録りながら数えて残す。
/// </summary>
public sealed class DropLogFileEntity
{
    public long Id { get; set; }

    public long RecordedId { get; set; }

    public string ParentDirectoryName { get; set; } = string.Empty;

    public string Filename { get; set; } = string.Empty;

    public long ErrorCount { get; set; }

    public long DropCount { get; set; }

    public long ScramblingCount { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public RecordedEntity? Recorded { get; set; }
}
