namespace TNLAStation.Infrastructure.Configuration;

public sealed class RecordingOptions
{
    public const string SectionName = "Recording";

    /// <summary>
    /// 時刻指定予約 (<c>IsTimeSpecified</c>) の開始マージン (秒)。EPGStation の
    /// <c>timeSpecifiedStartMargin</c> に相当し、番組表予約には適用しない — 番組表予約は
    /// 番組の開始時刻ちょうどに録り始める。
    /// </summary>
    public int StartMarginSeconds { get; init; } = 1;

    /// <summary>
    /// 時刻指定予約の終了マージン (秒)。EPGStation の <c>timeSpecifiedEndMargin</c> に相当し、
    /// 番組表予約には適用しない。
    /// </summary>
    public int EndMarginSeconds { get; init; } = 1;

    /// <summary>
    /// 予約表を見に行く間隔。開始時刻の判定はこの粒度になるので、開始マージンより短くする。
    /// </summary>
    public int PollIntervalSeconds { get; init; } = 5;

    /// <summary>
    /// 保存先。指定が無ければ Storage の最初の保存先を使う。
    /// </summary>
    public string? Directory { get; init; }

    /// <summary>
    /// 受信した TS のパケット欠け・エラー・スクランブル残りを数えて drop log を残すか。
    /// 数える処理自体に多少のコストがあるため、既定では EPGStation と同じく無効。
    /// </summary>
    public bool IsEnabledDropCheck { get; init; }

    /// <summary>
    /// 録画ファイル名のテンプレート。使えるプレースホルダは <see cref="Recording.RecordingFileName"/>
    /// を参照。既定値は EPGStation の <c>DEFAULT_VALUE.recordedFormat</c> と同一。
    /// 根拠: EPGStation/src/model/Configuration.ts。
    /// </summary>
    public string RecordedFormat { get; init; } = "%YEAR%年%MONTH%月%DAY%日%HOUR%時%MIN%分%SEC%秒-%TITLE%";

    public string RecordedFileExtension { get; init; } = ".ts";

    /// <summary>
    /// 録画中だけ使う一時保存先。設定すると、ここへ書きながら録り、完了後に
    /// <see cref="Directory"/> (または Storage の保存先) へ移す。EPGStation の
    /// <c>recordedTmp</c> に相当する。未設定なら最終保存先へ直接書く。
    /// </summary>
    public string? TempDirectory { get; init; }

    /// <summary>
    /// drop log (.drop.log) の保存先。未設定なら録画ファイルと同じディレクトリに置く。
    /// EPGStation の <c>dropLog</c> に相当する。
    /// </summary>
    public string? DropLogDirectory { get; init; }
}
