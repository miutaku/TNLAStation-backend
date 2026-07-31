using System.Net;
using System.Text;
using Microsoft.Extensions.Options;
using TNLAStation.Infrastructure.Configuration;
using TNLAStation.Infrastructure.Configuration.EpgStation;

namespace TNLAStation.Api.Endpoints;

/// <summary>
/// <c>GET /api/debug</c>。API 仕様書を読み込ませた Swagger UI へ送る。
///
/// EPGStation は <c>EPGStation/src/model/service/ServiceServer.ts</c> の <c>setSwaggerUI</c>:
/// <code>res.redirect(this.createUrl('/api-docs/?url=' + this.createUrl('/api/docs')))</code>
/// <c>createUrl</c> は subDirectory が無ければ引数そのまま、あれば <c>url-join</c> で結合する。
/// url-join は <c>/?</c> を <c>?</c> へ潰すので、subDirectory の有無で <c>/api-docs/?url=</c> と
/// <c>/api-docs?url=</c> のように 1 文字変わる。この差ごと再現する。
///
/// express の <c>res.redirect</c> は 302、<c>Location</c>、<c>Vary: Accept</c> を付け、
/// 本文に <c>Found. Redirecting to &lt;url&gt;</c> を書く (Accept 次第で HTML)。
/// </summary>
internal static class DebugEndpoints
{
    public static IEndpointRouteBuilder MapDebugEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/debug", (HttpContext context, IOptionsMonitor<ApiOptions> api) =>
        {
            string location = BuildLocation(api.CurrentValue.SubDirectory);
            return WriteRedirect(context, location);
        })
        .WithName("Debug")
        .WithSummary("API ドキュメントへのリダイレクト")
        .WithTags("debug")
        .ExcludeFromDescription();

        return endpoints;
    }

    internal static string BuildLocation(string? subDirectory)
    {
        string docs = CreateUrl(subDirectory, "/api/docs");
        return CreateUrl(subDirectory, "/api-docs/?url=" + docs);
    }

    private static string CreateUrl(string? subDirectory, string url) =>
        string.IsNullOrEmpty(subDirectory) ? url : UrlJoin.Join(subDirectory, url);

    private static async Task WriteRedirect(HttpContext context, string location)
    {
        HttpResponse response = context.Response;
        response.StatusCode = (int)HttpStatusCode.Found;
        response.Headers.Location = location;
        // express の res.format が付ける。表現が Accept で変わることを中継に伝える。
        response.Headers.Vary = "Accept";

        bool preferHtml = PrefersHtml(context.Request);
        string body = preferHtml
            ? $"<p>Found. Redirecting to <a href=\"{HtmlEscape(location)}\">{HtmlEscape(location)}</a></p>"
            : $"Found. Redirecting to {location}";

        response.ContentType = preferHtml ? "text/html; charset=utf-8" : "text/plain; charset=utf-8";
        byte[] bytes = Encoding.UTF8.GetBytes(body);
        response.ContentLength = bytes.Length;

        if (!HttpMethods.IsHead(context.Request.Method))
        {
            await response.Body.WriteAsync(bytes);
        }
    }

    /// <summary>
    /// express の <c>res.format({ text, html })</c> は登録順に候補を並べ、Accept と突き合わせる。
    /// 同じ品質なら先に登録した text/plain が勝つので、<c>*/*</c> や Accept 無しは text になる。
    /// </summary>
    private static bool PrefersHtml(HttpRequest request)
    {
        string accept = request.Headers.Accept.ToString();
        if (string.IsNullOrEmpty(accept))
        {
            return false;
        }

        double text = Quality(accept, "text/plain");
        double html = Quality(accept, "text/html");
        return html > text;
    }

    private static double Quality(string accept, string mediaType)
    {
        double best = -1;
        foreach (string part in accept.Split(','))
        {
            string[] pieces = part.Split(';');
            string type = pieces[0].Trim();
            bool matches = string.Equals(type, mediaType, StringComparison.OrdinalIgnoreCase)
                || string.Equals(type, "*/*", StringComparison.Ordinal)
                || string.Equals(type, mediaType.Split('/')[0] + "/*", StringComparison.OrdinalIgnoreCase);
            if (!matches)
            {
                continue;
            }

            double quality = 1;
            foreach (string parameter in pieces.Skip(1))
            {
                string trimmed = parameter.Trim();
                if (trimmed.StartsWith("q=", StringComparison.OrdinalIgnoreCase) &&
                    double.TryParse(trimmed[2..], System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out double parsed))
                {
                    quality = parsed;
                }
            }

            best = Math.Max(best, quality);
        }

        return best;
    }

    private static string HtmlEscape(string value) => value
        .Replace("&", "&amp;", StringComparison.Ordinal)
        .Replace("<", "&lt;", StringComparison.Ordinal)
        .Replace(">", "&gt;", StringComparison.Ordinal)
        .Replace("\"", "&quot;", StringComparison.Ordinal)
        .Replace("'", "&#39;", StringComparison.Ordinal);
}
