namespace TNLAStation.Infrastructure.Configuration;

/// <summary>
/// 予約・録画・エンコードの節目で外部コマンドを叩く。EPGStation の各 <c>*Command</c> 設定に
/// 対応する。未設定 (null) ならそのフックは何もしない。
/// </summary>
public sealed class CommandHookOptions
{
    public const string SectionName = "CommandHooks";

    public string? ReserveNewAdditionCommand { get; init; }

    public string? ReserveUpdateCommand { get; init; }

    public string? ReserveDeletedCommand { get; init; }

    public string? RecordingPreStartCommand { get; init; }

    public string? RecordingPrepRecFailedCommand { get; init; }

    public string? RecordingStartCommand { get; init; }

    public string? RecordingFinishCommand { get; init; }

    public string? RecordingFailedCommand { get; init; }

    public string? EncodingFinishCommand { get; init; }
}
