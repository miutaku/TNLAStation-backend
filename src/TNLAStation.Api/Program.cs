using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;
using Microsoft.OpenApi;
using TNLAStation.Api.Endpoints;
using TNLAStation.Api.Middleware;
using TNLAStation.Infrastructure.Configuration;
using TNLAStation.Infrastructure.DependencyInjection;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi(options =>
{
    options.OpenApiVersion = OpenApiSpecVersion.OpenApi3_0;
});
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    options.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
    options.SerializerOptions.PropertyNameCaseInsensitive = false;
});

builder.Services.AddTnlaStationInfrastructure(builder.Configuration);

WebApplication app = builder.Build();

app.UseMiddleware<EpgStationExceptionMiddleware>();

app.Use(async (context, next) =>
{
    context.Response.OnStarting(() =>
    {
        // EPGStation sets these headers in its JSON response helper, so they accompany successful
        // JSON payloads only: errors, files, and streams are left untouched.
        if (context.Response.StatusCode is >= 200 and < 300 && IsCompatibilityJsonResponse(context))
        {
            context.Response.Headers.CacheControl = "private, no-cache, no-store, must-revalidate";
            context.Response.Headers.Expires = "-1";
            context.Response.Headers.Pragma = "no-cache";
        }

        return Task.CompletedTask;
    });

    await next(context);
});

MapStreamFiles(app);

app.MapOpenApi(OpenApiDocumentPath);
app.MapEpgStationEndpoints();
app.MapEpgPhaseTwoEndpoints();
app.MapRuleEndpoints();
app.MapCollectionEndpoints();
app.MapReserveEndpoints();
app.MapRecordedEndpoints();
app.MapVideoEndpoints();
app.MapThumbnailEndpoints();
app.MapIptvEndpoints();
app.MapStreamEndpoints();

app.Run();

static void MapStreamFiles(WebApplication app)
{
    StreamingOptions streaming = app.Services.GetRequiredService<IOptions<StreamingOptions>>().Value;
    Directory.CreateDirectory(streaming.WorkDirectory);

    var contentTypes = new FileExtensionContentTypeProvider();
    contentTypes.Mappings[".m3u8"] = "application/vnd.apple.mpegurl";
    contentTypes.Mappings[".ts"] = "video/mp2t";

    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = new PhysicalFileProvider(streaming.WorkDirectory),
        RequestPath = "/streamfiles",
        ContentTypeProvider = contentTypes,
        ServeUnknownFileTypes = false,
        OnPrepareResponse = context =>
        {
            // プレイリストは数秒ごとに中身が変わる。キャッシュされると再生が止まったまま進まない。
            context.Context.Response.Headers.CacheControl = "no-store";
        },
    });
}

static bool IsCompatibilityJsonResponse(HttpContext context)
{
    PathString path = context.Request.Path;
    return path.StartsWithSegments("/api", StringComparison.Ordinal) &&
        !path.Equals(OpenApiDocumentPath, StringComparison.Ordinal) &&
        MediaTypeHeaderValue.TryParse(context.Response.ContentType, out MediaTypeHeaderValue? contentType) &&
        contentType.MatchesMediaType("application/json");
}

public partial class Program
{
    private const string OpenApiDocumentPath = "/api/docs";
}
