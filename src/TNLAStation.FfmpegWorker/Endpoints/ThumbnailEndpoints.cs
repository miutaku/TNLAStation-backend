using TNLAStation.FfmpegWorker.Contracts;
using TNLAStation.FfmpegWorker.Media;

namespace TNLAStation.FfmpegWorker.Endpoints;

public static class ThumbnailEndpoints
{
    public static void MapThumbnailEndpoints(this WebApplication app)
    {
        app.MapPost("/thumbnail", async (ThumbnailRequest request, ThumbnailRunner runner, CancellationToken cancellationToken) =>
        {
            (bool success, string? error) = await runner.ExtractAsync(
                request.InputPath,
                request.OutputPath,
                request.Width,
                request.Height,
                request.PositionSeconds,
                request.Command,
                cancellationToken);
            return Results.Ok(new ThumbnailResponse(success, error));
        });
    }
}
