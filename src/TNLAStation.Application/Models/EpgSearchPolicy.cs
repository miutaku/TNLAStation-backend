using System.Text.RegularExpressions;
using TNLAStation.Domain;

namespace TNLAStation.Application.Models;

public static class EpgSearchPolicy
{
    public static void Validate(EpgSearchQuery query)
    {
        if (!HasSearchCondition(query))
        {
            throw new InvalidOperationException("InvalidFindRuleOption");
        }

        if (query.Keyword is not null && !query.Name && !query.Description && !query.Extended)
        {
            throw new InvalidOperationException("InvalidFindRuleOption");
        }

        if (query.IgnoreKeyword is not null &&
            !query.IgnoreName &&
            !query.IgnoreDescription &&
            !query.IgnoreExtended)
        {
            throw new InvalidOperationException("InvalidFindRuleOption");
        }

        if (query.Times is { Count: > 0 } && query.Times.All(time => (time.Week & 0x7f) == 0))
        {
            throw new InvalidOperationException("InvalidFindRuleOption");
        }

        if (query.Limit is < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(query), "limit must not be negative");
        }
    }

    public static bool HasSearchCondition(EpgSearchQuery query)
    {
        if (query.Keyword is not null || query.IgnoreKeyword is not null)
        {
            return true;
        }

        if (query.ChannelIds is { Count: > 0 })
        {
            return true;
        }

        if (query.ChannelIds is null && (query.Gr || query.Bs || query.Cs || query.Sky))
        {
            return true;
        }

        return query.Genres is { Count: > 0 } ||
            query.Times is { Count: > 0 } ||
            query.IsFree ||
            query.DurationMin is not null ||
            query.DurationMax is not null ||
            query.SearchPeriods is { Count: > 0 };
    }

    public static bool Matches(EpgProgram program, EpgSearchQuery query, DateTimeOffset now)
    {
        if (program.EndAt < now)
        {
            return false;
        }

        if (!MatchesKeyword(program, query.Keyword, query.KeyCaseSensitive, query.KeyRegularExpression,
                query.Name, query.Description, query.Extended))
        {
            return false;
        }

        if (query.IgnoreKeyword is not null &&
            MatchesKeyword(program, query.IgnoreKeyword, query.IgnoreKeyCaseSensitive,
                query.IgnoreKeyRegularExpression, query.IgnoreName, query.IgnoreDescription,
                query.IgnoreExtended))
        {
            return false;
        }

        if (!MatchesChannel(program, query) || !MatchesGenres(program, query.Genres) ||
            !MatchesTimes(program, query.Times))
        {
            return false;
        }

        if (query.IsFree && !program.IsFree)
        {
            return false;
        }

        if (query.DurationMin is not null && program.DurationMilliseconds < query.DurationMin * 1000L)
        {
            return false;
        }

        if (query.DurationMax is not null && program.DurationMilliseconds > query.DurationMax * 1000L)
        {
            return false;
        }

        return query.SearchPeriods is not { Count: > 0 } || query.SearchPeriods.Any(period =>
            program.StartAt >= period.StartAt && program.StartAt <= period.EndAt);
    }

    private static bool MatchesKeyword(
        EpgProgram program,
        string? keyword,
        bool caseSensitive,
        bool regularExpression,
        bool searchName,
        bool searchDescription,
        bool searchExtended)
    {
        if (keyword is null)
        {
            return true;
        }

        var fields = new List<string>(3);
        if (searchName)
        {
            fields.Add(program.HalfWidthName);
        }

        if (searchDescription)
        {
            fields.Add(program.HalfWidthDescription ?? string.Empty);
        }

        if (searchExtended)
        {
            fields.Add(program.HalfWidthExtended ?? string.Empty);
        }

        if (fields.Count == 0)
        {
            return false;
        }

        if (regularExpression)
        {
            RegexOptions options = RegexOptions.CultureInvariant;
            if (!caseSensitive)
            {
                options |= RegexOptions.IgnoreCase;
            }

            var expression = new Regex(keyword, options, TimeSpan.FromSeconds(1));
            return fields.Any(expression.IsMatch);
        }

        string[] terms = EpgStringNormalizer.ToHalfWidth(keyword).Split(' ');
        StringComparison comparison = caseSensitive
            ? StringComparison.Ordinal
            : StringComparison.OrdinalIgnoreCase;
        return fields.Any(field => terms.All(term => field.Contains(term, comparison)));
    }

    private static bool MatchesChannel(EpgProgram program, EpgSearchQuery query)
    {
        if (query.ChannelIds is not null)
        {
            return query.ChannelIds.Count == 0 || query.ChannelIds.Contains(program.ChannelId);
        }

        var types = new List<string>(4);
        if (query.Gr)
        {
            types.Add("GR");
        }

        if (query.Bs)
        {
            types.Add("BS");
        }

        if (query.Cs)
        {
            types.Add("CS");
        }

        if (query.Sky)
        {
            types.Add("SKY");
        }

        return types.Count == 0 || types.Contains(program.ChannelType, StringComparer.Ordinal);
    }

    private static bool MatchesGenres(EpgProgram program, IReadOnlyList<EpgSearchGenre>? genres)
    {
        if (genres is not { Count: > 0 })
        {
            return true;
        }

        return genres.Any(genre =>
            MatchesGenre(program.Genre1, program.SubGenre1, genre) ||
            MatchesGenre(program.Genre2, program.SubGenre2, genre) ||
            MatchesGenre(program.Genre3, program.SubGenre3, genre));
    }

    private static bool MatchesGenre(int? genre, int? subGenre, EpgSearchGenre expected) =>
        genre == expected.Genre && (expected.SubGenre is null || subGenre == expected.SubGenre);

    private static bool MatchesTimes(EpgProgram program, IReadOnlyList<EpgSearchTime>? times)
    {
        if (times is not { Count: > 0 })
        {
            return true;
        }

        bool hasUsableTime = false;
        foreach (EpgSearchTime time in times)
        {
            int weekdayMask = 1 << program.Week;
            if ((time.Week & 0x7f) == 0)
            {
                continue;
            }

            hasUsableTime = true;
            if ((time.Week & weekdayMask) == 0)
            {
                continue;
            }

            if (time.Start is null || time.Range is null)
            {
                return true;
            }

            int end = time.Start.Value + time.Range.Value - 1;
            for (int hour = time.Start.Value; hour <= end; hour++)
            {
                if (program.StartHour == ((hour % 24) + 24) % 24)
                {
                    return true;
                }
            }
        }

        return !hasUsableTime;
    }
}
