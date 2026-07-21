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

        streams.MapGet("/live/{channelId:long}/m2ts", GetLiveM2tsAsync)
            .WithName("GetLiveM2ts")
            .WithSummary("ライブ M2TS ストリーム")
            .WithTags("streams");

        streams.MapGet("/recorded/{videoFileId:long}/hls", StartRecordedHlsAsync)
            .WithName("StartRecordedHls")
            .WithSummary("録画 HLS 配信を開始")
            .WithTags("streams")
            .Produces<StartStreamResponse>();

        streams.MapPut("/{streamId:long}/keep", KeepStream)
            .WithName("KeepStream")
            .WithSummary("配信の維持")
            .WithTags("streams");

        streams.MapDelete("/", StopAllStreamsAsync)
            .WithName("StopAllStreams")
            .WithSummary("すべての配信を停止")
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

    /// <summary>
    /// 変換せずにそのまま流す。対応する再生機なら画質を落とさずに見られるし、変換の負荷も
    /// かからない。ブラウザーでは再生できないので、画面は HLS を使う。
    /// </summary>
    private static async Task<IResult> GetLiveM2tsAsync(
        long channelId,
        HttpContext context,
        ILiveStreamService streams,
        CancellationToken cancellationToken)
    {
        await using Stream source = await streams.OpenLiveStreamAsync(channelId, cancellationToken);
        context.Response.ContentType = "video/mp2t";
        // 放送は終わらないので長さは書けない。書けば、そこで切れたと受け取られる。
        await source.CopyToAsync(context.Response.Body, cancellationToken);
        return Results.Empty;
    }

    private static async Task<IResult> StartRecordedHlsAsync(
        long videoFileId,
        ILiveStreamService streams,
        [FromQuery] int mode = 0,
        [FromQuery] double ss = 0,
        CancellationToken cancellationToken = default)
    {
        long streamId = await streams.StartRecordedHlsAsync(videoFileId, ss, mode, cancellationToken);
        return Results.Ok(new StartStreamResponse(streamId));
    }

    private static IResult KeepStream(long streamId, ILiveStreamService streams) =>
        streams.Keep(streamId)
            ? Results.NoContent()
            : Results.Json(
                new ErrorResponse(StatusCodes.Status404NotFound, "stream is not found"),
                statusCode: StatusCodes.Status404NotFound);

    private static async Task<IResult> StopAllStreamsAsync(ILiveStreamService streams)
    {
        await streams.StopAllAsync();
        return Results.NoContent();
    }

    private static async Task<IResult> StopStreamAsync(long streamId, ILiveStreamService streams)
    {
        // 既に畳んだ配信を止め直すのは失敗ではない。画面を閉じたときの停止要求は
        // 回収の後から届くことがある。
        await streams.StopAsync(streamId);
        return Results.NoContent();
    }
}
