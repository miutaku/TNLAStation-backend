using TNLAStation.FfmpegWorker.Contracts;
using TNLAStation.FfmpegWorker.Media;

namespace TNLAStation.FfmpegWorker.Endpoints;

public static class ProbeEndpoints
{
    public static void MapProbeEndpoints(this WebApplication app)
    {
        app.MapPost("/probe", async (ProbeRequest request, MediaProbeRunner probe, CancellationToken cancellationToken) =>
        {
            double? seconds = await probe.GetDurationSecondsAsync(request.Path, cancellationToken);
            return Results.Ok(new ProbeResponse(seconds));
        });
    }
}
