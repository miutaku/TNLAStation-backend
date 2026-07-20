using Microsoft.AspNetCore.Mvc;
using TNLAStation.Api.Contracts;
using TNLAStation.Application.Abstractions;

namespace TNLAStation.Api.Endpoints;

/// <summary>
/// ライブ視聴。開始した配信は stream id で追跡し、keep が届く間だけ生かす。
/// </summary>
internal static class StreamEndpoints
{
    public static IEndpointRouteBuilder MapStreamEndpoints(this IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder streams = endpoints.MapGroup("/api/streams");

        streams.MapGet("/live/{channelId:long}/hls", StartLiveHlsAsync)
            .WithName("StartLiveHls")
            .WithSummary("ライブ HLS 配信を開始")
            .WithTags("streams")
            .Produces<StartStreamResponse>();

        streams.MapPut("/{streamId:long}/keep", KeepStream)
            .WithName("KeepStream")
            .WithSummary("配信の維持")
            .WithTags("streams");

        streams.MapDelete("/{streamId:long}", StopStreamAsync)
            .WithName("StopStream")
            .WithSummary("配信の停止")
            .WithTags("streams");

        return endpoints;
    }

    private static async Task<IResult> StartLiveHlsAsync(
        long channelId,
        ILiveStreamService streams,
        [FromQuery] int mode = 0,
        CancellationToken cancellationToken = default)
    {
        long streamId = await streams.StartHlsAsync(channelId, mode, cancellationToken);
        return Results.Ok(new StartStreamResponse(streamId));
    }

    private static IResult KeepStream(long streamId, ILiveStreamService streams) =>
        streams.Keep(streamId)
            ? Results.NoContent()
            : Results.Json(
                new ErrorResponse(StatusCodes.Status404NotFound, "stream is not found"),
                statusCode: StatusCodes.Status404NotFound);

    private static async Task<IResult> StopStreamAsync(long streamId, ILiveStreamService streams)
    {
        // 既に畳んだ配信を止め直すのは失敗ではない。画面を閉じたときの停止要求は
        // 回収の後から届くことがある。
        await streams.StopAsync(streamId);
        return Results.NoContent();
    }
}
