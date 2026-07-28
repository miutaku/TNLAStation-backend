using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;
using Microsoft.OpenApi;
using TNLAStation.Api.Endpoints;
using TNLAStation.Api.Middleware;
using TNLAStation.Api.SocketIo;
using TNLAStation.Application.Abstractions;
using TNLAStation.Infrastructure.Configuration;
using TNLAStation.Infrastructure.Configuration.EpgStation;
using TNLAStation.Infrastructure.DependencyInjection;
using TNLAStation.Infrastructure.Logging;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// EPGStation 形式の config.yml を第一級の入力として読む。見つからなければ従来どおり
// appsettings / 環境変数だけで動く (後方互換)。config.yml は後から足すので、同じキーは
// config.yml が勝つ。
string? epgStationConfigPath = ResolveEpgStationConfigPath(builder.Configuration, builder.Environment.ContentRootPath);
if (epgStationConfigPath is not null)
{
    builder.Configuration.AddEpgStationConfigFile(epgStationConfigPath);
    // コンテナの内部待受ポートなど、デプロイ環境固有の値は config.yml より環境変数を
    // 優先できるようにする。通常の単体起動では環境変数が無ければ config.yml がそのまま有効。
    builder.Configuration.AddEnvironmentVariables();
    builder.Logging.AddEpgStationLogConfigs(Path.GetDirectoryName(epgStationConfigPath)!);
}

builder.Services.AddOpenApi(options =>
{
    options.OpenApiVersion = OpenApiSpecVersion.OpenApi3_0;
    options.AddDocumentTransformer((document, context, cancellationToken) =>
    {
        ApiOptions api = context.ApplicationServices.GetRequiredService<IOptions<ApiOptions>>().Value;
        if (api.Servers.Count > 0)
        {
            document.Servers = [.. api.Servers.Select(url => new OpenApiServer { Url = url })];
        }

        return Task.CompletedTask;
    });
});
builder.Services.AddCors();
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    options.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
    options.SerializerOptions.PropertyNameCaseInsensitive = false;
});

builder.Services.AddSingleton<SocketIoHub>();
builder.Services.AddSingleton<IClientNotifier, SocketIoClientNotifier>();
builder.Services.AddTnlaStationInfrastructure(builder.Configuration);

// socket.io を別ポートで待つ構成 (socketioPort != port) では、その待受も開く。
ServerOptions serverOptions = builder.Configuration.GetSection(ServerOptions.SectionName).Get<ServerOptions>()
    ?? new ServerOptions();
int? dedicatedSocketIoPort = SocketIoListener.ResolveDedicatedPort(serverOptions);
int? dedicatedHttpsSocketIoPort = SocketIoListener.ResolveDedicatedHttpsPort(serverOptions);
if (serverOptions.Port > 0 || serverOptions.Https?.Port is > 0 ||
    dedicatedSocketIoPort is not null || dedicatedHttpsSocketIoPort is not null)
{
    builder.WebHost.ConfigureKestrel(kestrel =>
    {
        if (serverOptions.Port > 0)
        {
            kestrel.ListenAnyIP(serverOptions.Port);
        }

        if (dedicatedSocketIoPort is { } socketPort)
        {
            kestrel.ListenAnyIP(socketPort);
        }

        if (serverOptions.Https is { Port: > 0 } https)
        {
            kestrel.ListenAnyIP(https.Port.Value, listen => ConfigureHttps(listen, https));
        }

        if (dedicatedHttpsSocketIoPort is { } secureSocketPort && serverOptions.Https is { } secure)
        {
            kestrel.ListenAnyIP(secureSocketPort, listen => ConfigureHttps(listen, secure));
        }
    });
}

WebApplication app = builder.Build();

ApiOptions api = app.Services.GetRequiredService<IOptions<ApiOptions>>().Value;

// EPGStation の専用 socket.io server は API application を載せない。Kestrel は同じ
// application pipeline を全 endpoint で共有するため、専用ポートでは socket.io 以外を
// 明示的に遮断する。
HashSet<int> socketOnlyPorts = [.. new[] { dedicatedSocketIoPort, dedicatedHttpsSocketIoPort }
    .Where(port => port is not null)
    .Select(port => port!.Value)];
if (socketOnlyPorts.Count > 0)
{
    app.Use(async (context, next) =>
    {
        if (context.Connection.LocalPort is var localPort &&
            socketOnlyPorts.Contains(localPort) &&
            !context.Request.Path.StartsWithSegments("/socket.io", StringComparison.OrdinalIgnoreCase) &&
            (string.IsNullOrWhiteSpace(api.SubDirectory) ||
             !context.Request.Path.StartsWithSegments(
                 $"{api.SubDirectory.TrimEnd('/')}/socket.io",
                 StringComparison.OrdinalIgnoreCase)))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        await next(context);
    });
}
if (!string.IsNullOrWhiteSpace(api.SubDirectory))
{
    // リバースプロキシがこのプレフィックスを削らずに転送してくる前提。以降のルーティング・
    // 生成 URL は全部このプレフィックスの内側で完結する。
    app.UsePathBase(api.SubDirectory);

    // subDirectory を設定した EPGStation は、その配下にしか何も生やさない
    // (ServiceServer.createUrl を通したパスだけを app.use / app.get する)。
    // UsePathBase だけだと接頭辞の有無どちらでも同じ経路に入ってしまい、上流が 404 を返す
    // URL が 200 になる。プレフィックスの外側は明示的に落とす。
    string prefix = api.SubDirectory;
    app.Use(async (context, next) =>
    {
        if (!context.Request.PathBase.Equals(prefix, StringComparison.OrdinalIgnoreCase))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        await next(context);
    });
}

if (api.IsAllowAllCors)
{
    app.UseCors(policy => policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());
}

app.UseWebSockets();
app.UseMiddleware<EpgStationExceptionMiddleware>();
app.UseMiddleware<OpenApiRequestValidationMiddleware>();
app.UseMiddleware<OpenApiValidationResponseMiddleware>();
app.UseMiddleware<SocketIoNotifyMiddleware>();

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

app.Use(async (context, next) =>
{
    await next(context);
    if (context.Response.StatusCode != StatusCodes.Status404NotFound ||
        context.Response.HasStarted ||
        context.Response.ContentType is not null ||
        !context.Request.Path.StartsWithSegments("/api", StringComparison.Ordinal))
    {
        return;
    }

    // Express の finalhandler と同じ未定義 API 応答。既知 API の 404 は各 endpoint が
    // JSON を設定済みなのでここには入らない。
    string requestPath = System.Text.Encodings.Web.HtmlEncoder.Default.Encode(context.Request.Path);
    string body = "<!DOCTYPE html>\n<html lang=\"en\">\n<head>\n<meta charset=\"utf-8\">\n" +
        "<title>Error</title>\n</head>\n<body>\n<pre>Cannot " +
        context.Request.Method + " " + requestPath + "</pre>\n</body>\n</html>\n";
    context.Response.ContentType = "text/html; charset=utf-8";
    await context.Response.WriteAsync(body, context.RequestAborted);
});

MapStreamFiles(app);

app.MapCompatibilityOpenApi();
// EPGStation は同じ構成 (仕様書は /api/docs、UI は /api-docs) を採る。パスの互換も揃える。
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint(OpenApiDocumentPath, "TNLAStation API");
    options.RoutePrefix = "api-docs";
});
app.MapSocketIoEndpoints();
app.MapDebugEndpoints();
app.MapEpgStationEndpoints();
app.MapEpgPhaseTwoEndpoints();
app.MapRuleEndpoints();
app.MapCollectionEndpoints();
app.MapReserveEndpoints();
app.MapRecordedEndpoints();
app.MapVideoEndpoints();
app.MapThumbnailEndpoints();
app.MapIptvEndpoints();
app.MapDropLogEndpoints();
app.MapStreamEndpoints();

app.Run();

static void ConfigureHttps(
    Microsoft.AspNetCore.Server.Kestrel.Core.ListenOptions listen,
    ServerHttpsOptions https)
{
    if (string.IsNullOrWhiteSpace(https.Cert) || string.IsNullOrWhiteSpace(https.Key))
    {
        throw new InvalidOperationException("HTTPS cert and key are required.");
    }

    listen.UseHttps(options =>
    {
        options.ServerCertificate = System.Security.Cryptography.X509Certificates.X509Certificate2
            .CreateFromPemFile(https.Cert, https.Key);
        if (https.Ca is { Count: > 0 })
        {
            options.ClientCertificateMode = ClientCertificateMode.RequireCertificate;
        }
    });
}

/// <summary>
/// config.yml の場所を決める。明示指定 (<c>EpgStationConfig</c> 設定キー、または
/// <c>EPGSTATION_CONFIG</c> 環境変数) を優先し、無ければ EPGStation と同じ
/// <c>&lt;ルート&gt;/config/config.yml</c> を探す。
/// </summary>
static string? ResolveEpgStationConfigPath(IConfiguration configuration, string contentRoot)
{
    string? explicitPath = configuration["EpgStationConfig"]
        ?? Environment.GetEnvironmentVariable("EPGSTATION_CONFIG");
    if (!string.IsNullOrWhiteSpace(explicitPath))
    {
        return explicitPath;
    }

    foreach (string candidate in new[]
             {
                 Path.Combine(contentRoot, "config", "config.yml"),
                 Path.Combine(AppContext.BaseDirectory, "config", "config.yml"),
                 Path.Combine(contentRoot, "..", "..", "config", "config.yml"),
             })
    {
        if (File.Exists(candidate))
        {
            return Path.GetFullPath(candidate);
        }
    }

    return null;
}

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
