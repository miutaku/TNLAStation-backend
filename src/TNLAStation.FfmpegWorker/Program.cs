using Microsoft.Extensions.Options;
using TNLAStation.Application.Abstractions;
using TNLAStation.FfmpegWorker.Endpoints;
using TNLAStation.FfmpegWorker.Media;
using TNLAStation.FfmpegWorker.Mirakurun;
using TNLAStation.FfmpegWorker.Options;
using TNLAStation.FfmpegWorker.Processes;
using TNLAStation.FfmpegWorker.Streaming;
using TNLAStation.Infrastructure.Configuration.EpgStation;
using TNLAStation.Infrastructure.DependencyInjection;
using TNLAStation.Infrastructure.Logging;
using TNLAStation.Infrastructure.Transcoding;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

string? epgStationConfigPath = builder.Configuration["EpgStationConfig"]
    ?? Environment.GetEnvironmentVariable("EPGSTATION_CONFIG");
if (string.IsNullOrWhiteSpace(epgStationConfigPath))
{
    string[] candidates =
    [
        Path.Combine(builder.Environment.ContentRootPath, "config", "config.yml"),
        Path.Combine(AppContext.BaseDirectory, "config", "config.yml"),
        Path.Combine(builder.Environment.ContentRootPath, "..", "..", "config", "config.yml"),
    ];
    epgStationConfigPath = candidates.FirstOrDefault(File.Exists);
}

if (!string.IsNullOrWhiteSpace(epgStationConfigPath))
{
    builder.Configuration.AddEpgStationConfigFile(epgStationConfigPath);
    builder.Configuration.AddEnvironmentVariables();
    builder.Logging.AddEpgStationLogConfigs(Path.GetDirectoryName(epgStationConfigPath)!, worker: true);
}

builder.Services.Configure<FfmpegOptions>(builder.Configuration.GetSection(FfmpegOptions.SectionName));
builder.Services.PostConfigure<FfmpegOptions>(options =>
{
    options.FfmpegPath = ExecutablePathResolver.Resolve(options.FfmpegPath);
    options.FfprobePath = ExecutablePathResolver.Resolve(options.FfprobePath);
});
builder.Services.Configure<MirakurunOptions>(builder.Configuration.GetSection(MirakurunOptions.SectionName));

builder.Services.AddHttpClient<WorkerMirakurunClient>((serviceProvider, client) =>
{
    MirakurunOptions options = serviceProvider.GetRequiredService<IOptions<MirakurunOptions>>().Value;
    if (options.IsConfigured)
    {
        client.BaseAddress = new Uri(options.BaseUrl);
    }
});

builder.Services.AddSingleton<ProcessGate>();
builder.Services.AddSingleton<EncodeDrainState>();
builder.Services.AddSingleton<MediaProbeRunner>();
builder.Services.AddSingleton<ThumbnailRunner>();
builder.Services.AddSingleton<EncodeRunner>();
if (!string.IsNullOrWhiteSpace(builder.Configuration.GetConnectionString("PostgreSQL")))
{
    builder.Services.AddSingleton<IEncodeExecutor, LocalEncodeExecutor>();
    builder.Services.AddTnlaStationEncodeWorker(builder.Configuration);
}

builder.Services.AddSingleton<HlsSessionRegistry>();
builder.Services.AddSingleton<TranscodeStreamer>();

WebApplication app = builder.Build();

// capacity と activeViewing は backend の受け付け判断に使う。ここが唯一の情報源。
app.MapGet("/health", (EncodeDrainState drainState, ProcessGate gate, IConfiguration configuration) =>
    drainState.IsDraining
        ? Results.StatusCode(StatusCodes.Status503ServiceUnavailable)
        : Results.Ok(new
        {
            status = "ok",
            activeCount = drainState.ActiveCount,
            capacity = gate.Capacity,
            activeViewing = gate.ActiveViewing,
            nodeName = configuration["Worker:NodeName"],
        }));
app.MapPost("/internal/drain", async (EncodeDrainState drainState, HttpContext context) =>
{
    await drainState.DrainAsync(context.RequestAborted);
    return Results.Ok(new { status = "drained" });
});
app.MapProbeEndpoints();
app.MapThumbnailEndpoints();
app.MapEncodeEndpoints();
app.MapStreamEndpoints();

app.Run();

public partial class Program;
