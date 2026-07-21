using TNLAStation.Domain;

namespace TNLAStation.Application.Models;

/// <summary>
/// 予約がどこから来たか。手動予約はルール予約より優先してチューナーを取る。
/// 人が明示的に入れたものを、ルールの都合で落とさないため。
/// </summary>
public enum ReserveSource
{
    /// <summary>番組を選んで入れた予約。</summary>
    Manual,

    /// <summary>時刻と放送局を直接指定した予約。番組表に無い放送も録れる。</summary>
    TimeSpecified,

    /// <summary>ルールが番組表と照合して作った予約。</summary>
    Rule,
}

/// <summary>
/// チューナー 1 本。受信できる放送波の種別だけが割り当てに効く。
/// </summary>
public sealed record TunerDevice(int Index, IReadOnlyList<string> ChannelTypes);

/// <summary>
/// 人が入れた予約。ルールと違い、番組表を引き直しても消えない。
/// </summary>
public sealed record ManualReserve(
    long Id,
    long ChannelId,
    string ChannelType,
    DateTimeOffset StartAt,
    DateTimeOffset EndAt,
    string Name,
    long? ProgramId = null,
    bool IsTimeSpecified = false,
    bool IsSkip = false,
    int Priority = ReservePriority.Normal);

/// <summary>
/// 重複判定に使う録画済みの履歴。ルールが同じ番組を録り直さないための材料。
/// </summary>
public sealed record RecordedHistoryItem(string Name, long ChannelId, DateTimeOffset EndAt);

/// <summary>
/// 予約 1 件分の、割り当てに必要な情報。番組表由来でも時刻指定でも形は同じ。
/// </summary>
public sealed record ReserveTarget(
    ReserveSource Source,
    long ChannelId,
    string ChannelType,
    DateTimeOffset StartAt,
    DateTimeOffset EndAt,
    string Name,
    long? ProgramId = null,
    long? RuleId = null,
    long? ManualReserveId = null,
    bool IsSkip = false,
    bool IsOverlap = false,
    int Priority = ReservePriority.Normal)
{
    /// <summary>
    /// 生成をやり直しても変わらない鍵。skip の指定は予約そのものではなくこの鍵に紐づくので、
    /// 番組表が更新されても人の意思が消えない。
    /// </summary>
    public string Key => ManualReserveId is { } manualId
        ? $"manual:{manualId}"
        : $"rule:{RuleId}:{ProgramId}";
}

/// <summary>
/// 割り当ての結果。チューナーが取れなかった予約は録画されないので、その旨を持たせる。
/// </summary>
public sealed record ReserveAssignment(ReserveTarget Target, int? TunerIndex)
{
    /// <summary>チューナーが空いておらず録画できない。</summary>
    public bool IsConflict => TunerIndex is null && !Target.IsSkip && !Target.IsOverlap;
}

/// <summary>
/// 生成の入力一式。
/// </summary>
public sealed record ReserveGenerationInput(
    DateTimeOffset Now,
    IReadOnlyList<RecordingRule> Rules,
    IReadOnlyList<EpgProgram> Programs,
    IReadOnlyList<ManualReserve> ManualReserves,
    IReadOnlyList<TunerDevice> Tuners,
    IReadOnlyList<RecordedHistoryItem> History,
    ReserveStates? States = null);

/// <summary>
/// 予約に対して人が示した意思。鍵で引く。予約は作り直されるので、予約の側には持てない。
/// </summary>
public sealed record ReserveStates(
    IReadOnlySet<string> Skipped,
    IReadOnlySet<string> OverlapCleared)
{
    public static ReserveStates Empty { get; } = new(new HashSet<string>(), new HashSet<string>());
}
