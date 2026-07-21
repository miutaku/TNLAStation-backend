using System.Text.Json.Serialization;
using TNLAStation.Application.Models;

namespace TNLAStation.Api.Contracts;

public sealed record RulesResponse(IReadOnlyList<RuleResponse> Rules, int Total);

public sealed record RuleResponse(
    long Id,
    bool IsTimeSpecification,
    RuleSearchOptionResponse SearchOption,
    RuleReserveOptionResponse ReserveOption)
{
    public ReserveSaveOptionResponse? SaveOption { get; init; }

    public ReserveEncodeOptionResponse? EncodeOption { get; init; }

    /// <summary>
    /// EPGStation only counts reserves when the caller asks for a reserve type.
    /// </summary>
    public int? ReservesCnt { get; init; }
}

public sealed record RuleSearchOptionResponse(
    bool KeyCS,
    bool KeyRegExp,
    bool Name,
    bool Description,
    bool Extended,
    bool IgnoreKeyCS,
    bool IgnoreKeyRegExp,
    bool IgnoreName,
    bool IgnoreDescription,
    bool IgnoreExtended,
    [property: JsonPropertyName("GR")] bool Gr,
    [property: JsonPropertyName("BS")] bool Bs,
    [property: JsonPropertyName("CS")] bool Cs,
    [property: JsonPropertyName("SKY")] bool Sky,
    bool IsFree)
{
    public string? Keyword { get; init; }

    public string? IgnoreKeyword { get; init; }

    public IReadOnlyList<long>? ChannelIds { get; init; }

    public IReadOnlyList<SearchGenreResponse>? Genres { get; init; }

    public IReadOnlyList<SearchTimeResponse>? Times { get; init; }

    public int? DurationMin { get; init; }

    public int? DurationMax { get; init; }

    public IReadOnlyList<SearchPeriodResponse>? SearchPeriods { get; init; }
}

public sealed record SearchGenreResponse(int Genre, int? SubGenre = null);

public sealed record SearchTimeResponse(int Week, int? Start = null, int? Range = null);

public sealed record SearchPeriodResponse(long StartAt, long EndAt);

public sealed record RuleReserveOptionResponse(bool Enable, bool AllowEndLack, bool AvoidDuplicate)
{
    public int? PeriodToAvoidDuplicate { get; init; }

    public IReadOnlyList<long>? Tags { get; init; }

    /// <summary>チューナーが足りないときに、どれを先に取るか。大きいほうが先。</summary>
    public int Priority { get; init; }
}

public sealed record ReserveSaveOptionResponse
{
    public string? ParentDirectoryName { get; init; }

    public string? Directory { get; init; }

    public string? RecordedFormat { get; init; }
}

public sealed record ReserveEncodeOptionResponse(bool IsDeleteOriginalAfterEncode)
{
    public string? Mode1 { get; init; }

    public string? EncodeParentDirectoryName1 { get; init; }

    public string? Directory1 { get; init; }

    public string? Mode2 { get; init; }

    public string? EncodeParentDirectoryName2 { get; init; }

    public string? Directory2 { get; init; }

    public string? Mode3 { get; init; }

    public string? EncodeParentDirectoryName3 { get; init; }

    public string? Directory3 { get; init; }
}

public sealed record RuleKeywordItemResponse(long Id, string Keyword);

public sealed record RuleKeywordInfoResponse(IReadOnlyList<RuleKeywordItemResponse> Items);

public sealed record AddedRuleResponse(long RuleId);

public sealed record ResultCodeResponse(int Code);

public sealed class AddRuleRequest
{
    [JsonRequired]
    public bool IsTimeSpecification { get; init; }

    [JsonRequired]
    public required RuleSearchRequest SearchOption { get; init; }

    [JsonRequired]
    public required RuleReserveOptionRequest ReserveOption { get; init; }

    public ReserveSaveOptionRequest? SaveOption { get; init; }

    public ReserveEncodeOptionRequest? EncodeOption { get; init; }
}

public sealed class RuleReserveOptionRequest
{
    [JsonRequired]
    public bool Enable { get; init; }

    [JsonRequired]
    public bool AllowEndLack { get; init; }

    [JsonRequired]
    public bool AvoidDuplicate { get; init; }

    public int? PeriodToAvoidDuplicate { get; init; }

    public IReadOnlyList<long>? Tags { get; init; }

    public int Priority { get; init; }
}

internal static class RuleContractMapper
{
    public static RuleResponse ToResponse(this RecordingRule rule, bool includeReservesCount)
    {
        EpgSearchQuery search = rule.SearchOption;
        return new RuleResponse(
            rule.Id,
            rule.IsTimeSpecification,
            new RuleSearchOptionResponse(
                search.KeyCaseSensitive,
                search.KeyRegularExpression,
                search.Name,
                search.Description,
                search.Extended,
                search.IgnoreKeyCaseSensitive,
                search.IgnoreKeyRegularExpression,
                search.IgnoreName,
                search.IgnoreDescription,
                search.IgnoreExtended,
                search.Gr,
                search.Bs,
                search.Cs,
                search.Sky,
                search.IsFree)
            {
                Keyword = search.Keyword,
                IgnoreKeyword = search.IgnoreKeyword,
                ChannelIds = search.ChannelIds,
                Genres = search.Genres?.Select(genre => new SearchGenreResponse(genre.Genre, genre.SubGenre)).ToArray(),
                Times = search.Times?.Select(time => new SearchTimeResponse(time.Week, time.Start, time.Range)).ToArray(),
                DurationMin = search.DurationMin,
                DurationMax = search.DurationMax,
                SearchPeriods = search.SearchPeriods?.Select(period => new SearchPeriodResponse(
                    period.StartAt.ToUnixTimeMilliseconds(),
                    period.EndAt.ToUnixTimeMilliseconds())).ToArray()
            },
            new RuleReserveOptionResponse(
                rule.ReserveOption.Enable,
                rule.ReserveOption.AllowEndLack,
                rule.ReserveOption.AvoidDuplicate)
            {
                PeriodToAvoidDuplicate = rule.ReserveOption.PeriodToAvoidDuplicate,
                Tags = rule.ReserveOption.Tags,
                Priority = rule.ReserveOption.Priority
            })
        {
            SaveOption = rule.SaveOption is null
                ? null
                : new ReserveSaveOptionResponse
                {
                    ParentDirectoryName = rule.SaveOption.ParentDirectoryName,
                    Directory = rule.SaveOption.Directory,
                    RecordedFormat = rule.SaveOption.RecordedFormat
                },
            EncodeOption = rule.EncodeOption is null
                ? null
                : new ReserveEncodeOptionResponse(rule.EncodeOption.IsDeleteOriginalAfterEncode)
                {
                    Mode1 = rule.EncodeOption.Mode1,
                    EncodeParentDirectoryName1 = rule.EncodeOption.EncodeParentDirectoryName1,
                    Directory1 = rule.EncodeOption.Directory1,
                    Mode2 = rule.EncodeOption.Mode2,
                    EncodeParentDirectoryName2 = rule.EncodeOption.EncodeParentDirectoryName2,
                    Directory2 = rule.EncodeOption.Directory2,
                    Mode3 = rule.EncodeOption.Mode3,
                    EncodeParentDirectoryName3 = rule.EncodeOption.EncodeParentDirectoryName3,
                    Directory3 = rule.EncodeOption.Directory3
                },
            // Without a reserve store the count is always zero, which is what EPGStation reports
            // for a rule that has produced no reserves.
            ReservesCnt = includeReservesCount ? 0 : null
        };
    }

    public static RecordingRule ToRule(this AddRuleRequest request, long id = 0) =>
        new(
            id,
            request.IsTimeSpecification,
            request.SearchOption.ToSearchQuery(),
            new RuleReserveOption(
                request.ReserveOption.Enable,
                request.ReserveOption.AllowEndLack,
                request.ReserveOption.AvoidDuplicate,
                request.ReserveOption.PeriodToAvoidDuplicate,
                request.ReserveOption.Tags,
                request.ReserveOption.Priority),
            request.SaveOption is null
                ? null
                : new ReserveSaveSettings(
                    request.SaveOption.ParentDirectoryName,
                    request.SaveOption.Directory,
                    request.SaveOption.RecordedFormat),
            request.EncodeOption is null
                ? null
                : new ReserveEncodeSettings(
                    request.EncodeOption.Mode1,
                    request.EncodeOption.EncodeParentDirectoryName1,
                    request.EncodeOption.Directory1,
                    request.EncodeOption.Mode2,
                    request.EncodeOption.EncodeParentDirectoryName2,
                    request.EncodeOption.Directory2,
                    request.EncodeOption.Mode3,
                    request.EncodeOption.EncodeParentDirectoryName3,
                    request.EncodeOption.Directory3,
                    request.EncodeOption.IsDeleteOriginalAfterEncode));
}
