using TNLAStation.Domain;

namespace TNLAStation.Application.Models;

/// <summary>
/// Rule lookup rules shared by every store. EPGStation splits the keyword on spaces after
/// normalizing it to half width and requires every term to appear in the rule keyword.
/// </summary>
public static class RuleQueryPolicy
{
    /// <summary>
    /// The message EPGStation raises when a rule to update or toggle no longer exists.
    /// </summary>
    public const string MissingRuleError = "RuleIsNull";

    public static IReadOnlyList<string> SplitKeyword(string? keyword) =>
        string.IsNullOrEmpty(keyword)
            ? []
            : EpgStringNormalizer.ToHalfWidth(keyword)
                .Split(' ', StringSplitOptions.RemoveEmptyEntries);

    public static bool MatchesKeyword(string? ruleKeyword, string? keyword)
    {
        IReadOnlyList<string> terms = SplitKeyword(keyword);
        if (terms.Count == 0)
        {
            return true;
        }

        if (ruleKeyword is null)
        {
            return false;
        }

        string target = EpgStringNormalizer.ToHalfWidth(ruleKeyword);
        return terms.All(term => target.Contains(term, StringComparison.OrdinalIgnoreCase));
    }
}
