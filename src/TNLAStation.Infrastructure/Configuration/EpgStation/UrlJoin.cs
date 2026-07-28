using System.Text.RegularExpressions;

namespace TNLAStation.Infrastructure.Configuration.EpgStation;

/// <summary>
/// EPGStation が使う npm の <c>url-join@4.0.1</c> の <c>normalize</c> をそのまま移植したもの。
///
/// subDirectory の整形 (<c>Configuration.formatConfig</c>) と <c>/api/debug</c> のリダイレクト先
/// (<c>ServiceServer.createUrl</c>) は、どちらもこの関数の細かな癖 — 末尾スラッシュの畳み方、
/// <c>/?</c> の <c>?</c> への潰し、2 つ目以降の <c>?</c> の <c>&amp;</c> 化 — がそのまま外部に見える。
/// 「だいたい同じ」パス結合で置き換えると Location が 1 文字ずれるので、規則ごと写している。
/// </summary>
public static class UrlJoin
{
    private static readonly Regex PlainProtocol = new(@"^[^/:]+:/*$", RegexOptions.Compiled);
    private static readonly Regex FileProtocol = new(@"^file:///", RegexOptions.Compiled);
    private static readonly Regex ProtocolPrefix = new(@"^([^/:]+):/*", RegexOptions.Compiled);
    private static readonly Regex LeadingSlashes = new(@"^/+", RegexOptions.Compiled);
    private static readonly Regex TrailingSlashes = new(@"/+$", RegexOptions.Compiled);
    private static readonly Regex SlashBeforeQuery = new(@"/(\?|&|#[^!])", RegexOptions.Compiled);

    public static string Join(params string[] parts)
    {
        ArgumentNullException.ThrowIfNull(parts);

        if (parts.Length == 0)
        {
            return string.Empty;
        }

        List<string> input = [.. parts];

        // 先頭がプロトコルだけの場合は次の要素と繋げる ("http://" + "example.com")。
        if (PlainProtocol.IsMatch(input[0]) && input.Count > 1)
        {
            string first = input[0];
            input.RemoveAt(0);
            input[0] = first + input[0];
        }

        input[0] = FileProtocol.IsMatch(input[0])
            ? ProtocolPrefix.Replace(input[0], "$1:///", 1)
            : ProtocolPrefix.Replace(input[0], "$1://", 1);

        List<string> result = [];
        for (int i = 0; i < input.Count; i++)
        {
            string component = input[i];
            if (component.Length == 0)
            {
                continue;
            }

            if (i > 0)
            {
                component = LeadingSlashes.Replace(component, string.Empty, 1);
            }

            component = i < input.Count - 1
                ? TrailingSlashes.Replace(component, string.Empty, 1)
                : TrailingSlashes.Replace(component, "/", 1);

            result.Add(component);
        }

        string joined = string.Join('/', result);
        joined = SlashBeforeQuery.Replace(joined, "$1");

        // 2 つ目以降の '?' は '&' に読み替える。
        string[] segments = joined.Split('?');
        return segments.Length <= 1
            ? joined
            : segments[0] + "?" + string.Join('&', segments.Skip(1));
    }
}
