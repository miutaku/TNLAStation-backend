namespace TNLAStation.Application.Models;

/// <summary>
/// A recording rule. The search option is the same query the schedule search uses, so one policy
/// decides both what a search returns and what a rule reserves.
/// </summary>
public sealed record RecordingRule(
    long Id,
    bool IsTimeSpecification,
    EpgSearchQuery SearchOption,
    RuleReserveOption ReserveOption,
    ReserveSaveSettings? SaveOption = null,
    ReserveEncodeSettings? EncodeOption = null,
    long UpdateCount = 0);

public sealed record RuleReserveOption(
    bool Enable,
    bool AllowEndLack,
    bool AvoidDuplicate,
    int? PeriodToAvoidDuplicate = null,
    IReadOnlyList<long>? Tags = null);

public sealed record RuleKeywordItem(long Id, string Keyword);

public sealed record RuleQuery(
    int? Offset = null,
    int? Limit = null,
    string? Keyword = null,
    string? Type = null);
