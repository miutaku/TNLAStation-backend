using System.Text.Json.Nodes;
using TNLAStation.Infrastructure.Configuration.EpgStation;

namespace TNLAStation.Api.Endpoints;

internal static class CompatibilityOpenApiEndpoints
{
    private const string ResourceName =
        "TNLAStation.Api.Compatibility.epgstation-api-v2.10.0.json";

    private static readonly string Template = ReadTemplate();

    public static IEndpointRouteBuilder MapCompatibilityOpenApi(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/docs", GetAsync)
            .WithName("GetCompatibilityOpenApi")
            .ExcludeFromDescription();
        return endpoints;
    }

    private static IResult GetAsync(HttpContext context, IEpgStationConfigAccessor config)
    {
        JsonObject document = JsonNode.Parse(Template)!.AsObject();
        IReadOnlyList<string> configured = config.Current.ApiServers;
        string apiPath = UrlJoin.Join(config.Current.SubDirectory ?? string.Empty, "/api");
        string[] servers = configured.Count > 0
            ? [.. configured.Select(server => UrlJoin.Join(server, apiPath))]
            : [UrlJoin.Join($"{context.Request.Scheme}://{context.Request.Host}", apiPath)];

        document["servers"] = new JsonArray(
            [.. servers.Select(server => (JsonNode)new JsonObject { ["url"] = server })]);
        return Results.Json(document);
    }

    private static string ReadTemplate()
    {
        using Stream stream = typeof(CompatibilityOpenApiEndpoints).Assembly
            .GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Embedded resource {ResourceName} was not found.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
