namespace TNLAStation.Infrastructure.Persistence;

/// <summary>
/// エンコードの待ち行列。1 度に 1 本しか走らせないので、順番を持たせて残す。
/// 途中で落ちても、行が残っていれば起動後にやり直せる。
/// </summary>
public sealed class EncodeTaskEntity
{
    public long Id { get; set; }

    public long RecordedId { get; set; }

    public long SourceVideoFileId { get; set; }

    /// <summary>変換の設定名。config の encode に並ぶもの。</summary>
    public string Mode { get; set; } = string.Empty;

    public string? ParentDirectoryName { get; set; }

    public string? Directory { get; set; }

    public bool RemoveOriginal { get; set; }

    /// <summary>waiting か running。走っている最中に落ちた行は起動時に waiting へ戻す。</summary>
    public string Status { get; set; } = string.Empty;

    public int? Percent { get; set; }

    /// <summary>実行中の ffmpeg ログの末尾。長くなりすぎないよう新しい行だけを残す。</summary>
    public string? Log { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
