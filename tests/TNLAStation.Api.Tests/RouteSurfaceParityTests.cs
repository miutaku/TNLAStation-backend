using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace TNLAStation.Api.Tests;

/// <summary>
/// 公開しているルートの集合が EPGStation v2.10.0 と 1 件も違わないことを機械的に確かめる。
///
/// 上流の一覧は手で書き写さず、<c>EPGStation/src/model/service/api</c> のファイル配置と
/// 各ファイルの <c>export const get|post|put|del</c> から毎回作り直す。express-openapi は
/// ディレクトリ構成をそのままパスに、export 名をそのまま HTTP method に割り当てるため
/// (<c>ServiceServer.initOpenApi</c> の <c>paths: API_DIR</c>)、これが上流の定義そのもの。
/// EPGStation の作業ツリーが無い環境では、同じ一覧を写した表で照合する。
/// </summary>
public sealed class RouteSurfaceParityTests : IDisposable
{
    private readonly WebApplicationFactory<Program> factory = new();

    /// <summary>
    /// <c>src/model/service/api</c> の走査結果 (v2.10.0 / 5cf2ea383d37937eacecf424820dbd7a278d577e)。
    /// 上流ツリーがあるときは、この表自体が実配置と一致することも確かめる。
    /// </summary>
    private static readonly string[] UpstreamRoutes =
    [
        "GET /api/channels",
        "GET /api/channels/{channelId}/logo",
        "GET /api/config",
        "GET /api/dropLogs/{dropLogFileId}",
        "GET /api/encode",
        "POST /api/encode",
        "DELETE /api/encode/{encodeId}",
        "GET /api/iptv/channel.m3u8",
        "GET /api/iptv/epg.xml",
        "GET /api/recorded",
        "POST /api/recorded",
        "POST /api/recorded/cleanup",
        "GET /api/recorded/options",
        "GET /api/recorded/{recordedId}",
        "DELETE /api/recorded/{recordedId}",
        "DELETE /api/recorded/{recordedId}/encode",
        "PUT /api/recorded/{recordedId}/protect",
        "PUT /api/recorded/{recordedId}/unprotect",
        "GET /api/recording",
        "POST /api/recording/resettimer",
        "GET /api/reserves",
        "POST /api/reserves",
        "GET /api/reserves/cnts",
        "GET /api/reserves/lists",
        "POST /api/reserves/update",
        "GET /api/reserves/{reserveId}",
        "PUT /api/reserves/{reserveId}",
        "DELETE /api/reserves/{reserveId}",
        "DELETE /api/reserves/{reserveId}/overlap",
        "DELETE /api/reserves/{reserveId}/skip",
        "GET /api/rules",
        "POST /api/rules",
        "GET /api/rules/keyword",
        "POST /api/rules/keyword",
        "GET /api/rules/{ruleId}",
        "PUT /api/rules/{ruleId}",
        "DELETE /api/rules/{ruleId}",
        "PUT /api/rules/{ruleId}/disable",
        "PUT /api/rules/{ruleId}/enable",
        "GET /api/schedules",
        "GET /api/schedules/broadcasting",
        "POST /api/schedules/search",
        "GET /api/schedules/detail/{programId}",
        "GET /api/schedules/{channelId}",
        "GET /api/storages",
        "GET /api/streams",
        "DELETE /api/streams",
        "GET /api/streams/live/{channelId}/hls",
        "GET /api/streams/live/{channelId}/m2ts",
        "GET /api/streams/live/{channelId}/m2ts/playlist",
        "GET /api/streams/live/{channelId}/m2tsll",
        "GET /api/streams/live/{channelId}/mp4",
        "GET /api/streams/live/{channelId}/webm",
        "GET /api/streams/recorded/{videoFileId}/hls",
        "GET /api/streams/recorded/{videoFileId}/mp4",
        "GET /api/streams/recorded/{videoFileId}/webm",
        "DELETE /api/streams/{streamId}",
        "PUT /api/streams/{streamId}/keep",
        "GET /api/tags",
        "POST /api/tags",
        "PUT /api/tags/{tagId}",
        "DELETE /api/tags/{tagId}",
        "PUT /api/tags/{tagId}/relate",
        "DELETE /api/tags/{tagId}/relate",
        "POST /api/thumbnails",
        "POST /api/thumbnails/cleanup",
        "POST /api/thumbnails/videos/{videoFileId}",
        "GET /api/thumbnails/{thumbnailId}",
        "DELETE /api/thumbnails/{thumbnailId}",
        "GET /api/version",
        "POST /api/videos/upload",
        "GET /api/videos/{videoFileId}",
        "DELETE /api/videos/{videoFileId}",
        "GET /api/videos/{videoFileId}/duration",
        "POST /api/videos/{videoFileId}/kodi",
        "GET /api/videos/{videoFileId}/playlist",
    ];

    /// <summary>
    /// express-openapi の外で <c>ServiceServer</c> が直接生やすルート。
    /// <c>/api/docs</c> は <c>docsPath</c>、<c>/api/debug</c> は Swagger UI へのリダイレクト。
    /// </summary>
    private static readonly string[] UpstreamServerRoutes =
    [
        "GET /api/docs",
        "GET /api/debug",
    ];

    [Fact]
    public void TheApiSurfaceHasNoExtraAndNoMissingRoutes()
    {
        HashSet<string> expected = new(UpstreamRoutes.Concat(UpstreamServerRoutes), StringComparer.Ordinal);
        HashSet<string> actual = new(GetMappedApiRoutes(), StringComparer.Ordinal);

        string[] missing = [.. expected.Except(actual).Order(StringComparer.Ordinal)];
        string[] extra = [.. actual.Except(expected).Order(StringComparer.Ordinal)];

        Assert.True(
            missing.Length == 0 && extra.Length == 0,
            $"missing:{Environment.NewLine}  {string.Join($"{Environment.NewLine}  ", missing)}{Environment.NewLine}" +
            $"extra:{Environment.NewLine}  {string.Join($"{Environment.NewLine}  ", extra)}");
    }

    /// <summary>
    /// 上流の作業ツリーがあるときだけ走る。この試験が持つ表が、上流のファイル配置から
    /// 機械的に導いた一覧と一致することを確かめる — 表が古びたまま緑になるのを防ぐ。
    /// </summary>
    [Fact]
    public void TheExpectedListMatchesTheUpstreamSourceTreeWhenItIsAvailable()
    {
        string? apiDirectory = FindUpstreamApiDirectory();
        if (apiDirectory is null)
        {
            // EPGStation のソースを持たない環境 (CI の一部) では検証をとばす。上の試験は走る。
            return;
        }

        string[] derived = [.. ExtractUpstreamRoutes(apiDirectory).Order(StringComparer.Ordinal)];
        Assert.Equal([.. UpstreamRoutes.Order(StringComparer.Ordinal)], derived);
    }

    private IEnumerable<string> GetMappedApiRoutes()
    {
        var sources = factory.Services.GetRequiredService<IEnumerable<EndpointDataSource>>();
        foreach (Endpoint endpoint in sources.SelectMany(source => source.Endpoints))
        {
            if (endpoint is not RouteEndpoint route)
            {
                continue;
            }

            string path = "/" + route.RoutePattern.RawText?.TrimStart('/');
            if (!path.StartsWith("/api", StringComparison.Ordinal))
            {
                continue;
            }

            path = NormalisePath(path);
            HttpMethodMetadata? methods = endpoint.Metadata.GetMetadata<HttpMethodMetadata>();
            foreach (string method in methods?.HttpMethods ?? [])
            {
                yield return $"{method} {path}";
            }
        }
    }

    /// <summary>
    /// ASP.NET のルート制約 (<c>{id:long}</c>) と末尾スラッシュを落として、上流の書き方に揃える。
    /// </summary>
    private static string NormalisePath(string path)
    {
        string withoutConstraints = Regex.Replace(path, @"\{([A-Za-z0-9_]+)(?::[^}]+)?\}", "{$1}");
        return withoutConstraints.Length > 1
            ? withoutConstraints.TrimEnd('/')
            : withoutConstraints;
    }

    private static IEnumerable<string> ExtractUpstreamRoutes(string apiDirectory)
    {
        foreach (string file in Directory.EnumerateFiles(apiDirectory, "*.ts", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(apiDirectory, file).Replace(Path.DirectorySeparatorChar, '/');
            string path = "/api/" + relative[..^".ts".Length];
            foreach (Match match in Regex.Matches(File.ReadAllText(file), @"^export const (get|post|put|del)\b",
                         RegexOptions.Multiline))
            {
                string verb = match.Groups[1].Value;
                string method = verb switch
                {
                    "del" => "DELETE",
                    _ => verb.ToUpper(CultureInfo.InvariantCulture),
                };
                yield return $"{method} {path}";
            }
        }
    }

    private static string? FindUpstreamApiDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string candidate = Path.Combine(directory.FullName, "EPGStation", "src", "model", "service", "api");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        return null;
    }

    public void Dispose() => factory.Dispose();
}
