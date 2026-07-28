namespace TNLAStation.Application.Abstractions;

/// <summary>予約の追加・更新・削除、録画開始前後で渡す情報。EPGStation の Reserve 形の env に合わせる。</summary>
public sealed record ReserveHookPayload(
    long ReserveId,
    long? ProgramId,
    long ChannelId,
    string ChannelName,
    string HalfWidthChannelName,
    long StartAt,
    long EndAt,
    string Name,
    string HalfWidthName,
    string? Description = null,
    string? HalfWidthDescription = null,
    string? Extended = null,
    string? HalfWidthExtended = null,
    string? ChannelType = null);

/// <summary>録画の開始・終了・失敗で渡す情報。EPGStation の Recorded 形の env に合わせる。</summary>
public sealed record RecordedHookPayload(
    long RecordedId,
    long? ProgramId,
    long ChannelId,
    string ChannelName,
    string HalfWidthChannelName,
    long StartAt,
    long EndAt,
    string Name,
    string HalfWidthName,
    string? Description = null,
    string? HalfWidthDescription = null,
    string? Extended = null,
    string? HalfWidthExtended = null,
    string? RecPath = null,
    string? LogPath = null,
    long? ErrorCount = null,
    long? DropCount = null,
    long? ScramblingCount = null,
    string? ChannelType = null);

/// <summary>エンコード完了で渡す情報。</summary>
public sealed record EncodeFinishHookPayload(
    long RecordedId,
    long? VideoFileId,
    string? OutputPath,
    string Mode,
    long ChannelId,
    string ChannelName,
    string HalfWidthChannelName,
    string Name,
    string HalfWidthName,
    string? Description = null,
    string? HalfWidthDescription = null,
    string? Extended = null,
    string? HalfWidthExtended = null);

/// <summary>
/// 予約・録画・エンコードの節目で、設定された外部コマンドを実行する。コマンド未設定なら
/// 何もしない。プロセスの起動だけを行い、終了を待たない — 遅い/固まるスクリプトで
/// 予約や録画そのものの処理を止めないため。
/// </summary>
public interface ICommandHookRunner
{
    void RunReserveHook(string? command, ReserveHookPayload payload);

    void RunRecordedHook(string? command, RecordedHookPayload payload);

    void RunEncodeFinishHook(string? command, EncodeFinishHookPayload payload);
}
