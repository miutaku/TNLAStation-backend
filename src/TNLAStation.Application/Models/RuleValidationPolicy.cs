namespace TNLAStation.Application.Models;

/// <summary>
/// ルールの追加・更新時に EPGStation (ReserveOptionChecker.checkRuleOption) がかけている検査。
/// 番組検索そのものの検査 (<see cref="EpgSearchPolicy.Validate"/>, 空条件や
/// InvalidFindRuleOption) とは別物 — こちらはルールを保存できる形かどうかを見る。
/// EPGStation はここで落ちると AddRuleError/UpdateRuleError を投げ、汎用の 500 になる。
/// </summary>
public static class RuleValidationPolicy
{
    public static void Validate(
        RecordingRule rule,
        IReadOnlyCollection<string> encodeModeNames,
        bool hasEncodeConfig,
        string errorMessage)
    {
        if (!IsSearchOptionValid(rule.IsTimeSpecification, rule.SearchOption) ||
            !IsReserveOptionValid(rule.ReserveOption) ||
            !EncodeOptionValidationPolicy.IsValid(rule.EncodeOption, encodeModeNames, hasEncodeConfig))
        {
            throw new InvalidOperationException(errorMessage);
        }
    }

    private static bool IsSearchOptionValid(bool isTimeSpecification, EpgSearchQuery option)
    {
        if (isTimeSpecification)
        {
            if (option.Keyword is null || option.ChannelIds is null || option.Times is null)
            {
                return false;
            }

            return option.Times.All(time =>
                time.Start is { } start && time.Range is { } range && start >= 0 && range > 0);
        }

        if (!IsKeywordOptionValid(option.Keyword, option.KeyCaseSensitive, option.KeyRegularExpression,
                option.Name, option.Description, option.Extended) ||
            !IsKeywordOptionValid(option.IgnoreKeyword, option.IgnoreKeyCaseSensitive,
                option.IgnoreKeyRegularExpression, option.IgnoreName, option.IgnoreDescription,
                option.IgnoreExtended))
        {
            return false;
        }

        if (option.ChannelIds is not null && (option.Gr || option.Bs || option.Cs || option.Sky))
        {
            return false;
        }

        if (option.Genres is { Count: > 0 } &&
            option.Genres.Any(genre => !IsGenreCodeValid(genre.Genre) ||
                (genre.SubGenre is { } sub && !IsGenreCodeValid(sub))))
        {
            return false;
        }

        if (option.Times is { Count: > 0 })
        {
            foreach (EpgSearchTime time in option.Times)
            {
                if (time.Week == 0)
                {
                    return false;
                }

                if (time.Start is { } start && time.Range is { } range &&
                    (start is < 0 or > 23 || range is < 1 or > 23))
                {
                    return false;
                }
            }
        }

        if (option.DurationMin is < 0 || option.DurationMax is < 0)
        {
            return false;
        }

        return option.DurationMin is null || option.DurationMax is null ||
            option.DurationMin <= option.DurationMax;
    }

    private static bool IsGenreCodeValid(int code) => code is >= 0x00 and <= 0xf;

    private static bool IsKeywordOptionValid(
        string? keyword,
        bool caseSensitive,
        bool regularExpression,
        bool name,
        bool description,
        bool extended)
    {
        if (keyword is not null)
        {
            return name || description || extended;
        }

        return !(caseSensitive || regularExpression || name || description || extended);
    }

    private static bool IsReserveOptionValid(RuleReserveOption option) =>
        option.PeriodToAvoidDuplicate is null || option.AvoidDuplicate;
}
