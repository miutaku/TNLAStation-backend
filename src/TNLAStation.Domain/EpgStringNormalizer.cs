using System.Text.RegularExpressions;

namespace TNLAStation.Domain;

public static partial class EpgStringNormalizer
{
    private static readonly IReadOnlyDictionary<string, string> EnclosedCharacterReplacements =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["\U0001f14a"] = "[HV]",
            ["\U0001f13f"] = "[P]",
            ["\U0001f14c"] = "[SD]",
            ["\U0001f146"] = "[W]",
            ["\U0001f14b"] = "[MV]",
            ["\U0001f210"] = "[手]",
            ["\U0001f211"] = "[字]",
            ["\U0001f212"] = "[双]",
            ["\U0001f213"] = "[デ]",
            ["\U0001f142"] = "[S]",
            ["\U0001f214"] = "[二]",
            ["\U0001f215"] = "[多]",
            ["\U0001f216"] = "[解]",
            ["\U0001f14d"] = "[SS]",
            ["\U0001f131"] = "[B]",
            ["\U0001f13d"] = "[N]",
            ["\U0001f217"] = "[天]",
            ["\U0001f218"] = "[交]",
            ["\U0001f219"] = "[映]",
            ["\U0001f21a"] = "[無]",
            ["\U0001f21b"] = "[料]",
            ["\u26bf"] = "[鍵]",
            ["\U0001f21c"] = "[前]",
            ["\U0001f21d"] = "[後]",
            ["\U0001f21e"] = "[再]",
            ["\U0001f21f"] = "[新]",
            ["\U0001f220"] = "[初]",
            ["\U0001f221"] = "[終]",
            ["\U0001f222"] = "[生]",
            ["\U0001f223"] = "[販]",
            ["\U0001f224"] = "[声]",
            ["\U0001f225"] = "[吹]",
            ["\U0001f14e"] = "[PPV]",
            ["\u3299"] = "[秘]",
            ["\U0001f200"] = "[ほか]"
        };

    public static string RemoveDatabaseUnsupportedCharacters(string value) =>
        value.Replace("\0", string.Empty, StringComparison.Ordinal);

    public static string ReplaceEnclosedCharacters(string value)
    {
        foreach ((string source, string replacement) in EnclosedCharacterReplacements)
        {
            value = value.Replace(source, replacement, StringComparison.Ordinal);
        }

        return value;
    }

    public static string ToHalfWidth(string value)
    {
        char[] characters = value.ToCharArray();
        for (int index = 0; index < characters.Length; index++)
        {
            if (characters[index] is >= '\uff01' and <= '\uff5e')
            {
                characters[index] = (char)(characters[index] - 0xfee0);
            }
        }

        return new string(characters)
            .Replace('\u201d', '"')
            .Replace('\u2019', '\'')
            .Replace('\u2018', '`')
            .Replace('\uffe5', '\\')
            .Replace('\u3000', ' ')
            .Replace('\u301c', '~');
    }

    public static string DeleteBrackets(string value)
    {
        foreach (string source in EnclosedCharacterReplacements.Keys)
        {
            value = value.Replace(source, string.Empty, StringComparison.Ordinal);
        }

        return BracketExpression().Replace(value, string.Empty).Trim();
    }

    [GeneratedRegex("\\[.+?\\]", RegexOptions.CultureInvariant)]
    private static partial Regex BracketExpression();
}
