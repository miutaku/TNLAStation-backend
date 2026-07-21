using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using TNLAStation.Api.Contracts;
using TNLAStation.Application.Abstractions;
using TNLAStation.Domain;

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

        // mp4・webm・m2tsll。HLS と違って途中のファイルを作らず、1 本の流れとして配る。
        streams.MapGet("/live/{channelId:long}/{format:regex(^(mp4|webm|m2tsll)$)}", GetLiveTranscodedAsync)
            .WithName("GetLiveTranscodedStream")
            .WithSummary("ライブ配信 (変換)")
            .WithTags("streams");

        streams.MapGet("/recorded/{videoFileId:long}/{format:regex(^(mp4|webm)$)}", GetRecordedTranscodedAsync)
            .WithName("GetRecordedTranscodedStream")
            .WithSummary("録画配信 (変換)")
            .WithTags("streams");

        streams.MapGet("/live/{channelId:long}/m2ts/playlist", GetLiveM2tsPlaylistAsync)
            .WithName("GetLiveM2tsPlaylist")
            .WithSummary("ライブ M2TS のプレイリスト")
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

    private static async Task<IResult> GetLiveTranscodedAsync(
        long channelId,
        string format,
        HttpContext context,
        ILiveStreamService streams,
        [FromQuery] int mode = 0,
        CancellationToken cancellationToken = default)
    {
        TranscodedOutput output = await streams.OpenTranscodedLiveAsync(
            channelId,
            format,
            mode,
            cancellationToken);
        return await WriteAsync(context, output, cancellationToken);
    }

    private static async Task<IResult> GetRecordedTranscodedAsync(
        long videoFileId,
        string format,
        HttpContext context,
        ILiveStreamService streams,
        [FromQuery] int mode = 0,
        [FromQuery] double ss = 0,
        CancellationToken cancellationToken = default)
    {
        TranscodedOutput output = await streams.OpenTranscodedRecordedAsync(
            videoFileId,
            format,
            mode,
            ss,
            cancellationToken);
        return await WriteAsync(context, output, cancellationToken);
    }

    private static async Task<IResult> WriteAsync(
        HttpContext context,
        TranscodedOutput output,
        CancellationToken cancellationToken)
    {
        await using Stream content = output.Content;
        context.Response.ContentType = output.ContentType;
        // 変換しながら出すので長さは書けない。書けば、そこで切れたと受け取られる。
        await content.CopyToAsync(context.Response.Body, cancellationToken);
        return Results.Empty;
    }

    /// <summary>
    /// そのまま流す口を指す 1 枚。動画の URL を直接渡せない再生機のために挟む。
    /// </summary>
    private static async Task<IResult> GetLiveM2tsPlaylistAsync(
        long channelId,
        HttpContext context,
        IEpgRepository epg,
        [FromQuery] int mode = 0,
        CancellationToken cancellationToken = default)
    {
        EpgChannel? channel = await epg.GetChannelAsync(channelId, cancellationToken);
        if (channel is null)
        {
            return Results.Json(
                new ErrorResponse(StatusCodes.Status404NotFound, "channel is not found"),
                statusCode: StatusCodes.Status404NotFound);
        }

        HttpRequest request = context.Request;
        string id = channelId.ToString(CultureInfo.InvariantCulture);
        string playlist = string.Join(
            '\n',
            "#EXTM3U",
            $"#EXTINF:-1,{channel.Name}",
            $"{request.Scheme}://{request.Host}/api/streams/live/{id}/m2ts?mode={mode.ToString(CultureInfo.InvariantCulture)}",
            string.Empty);

        return Results.Text(playlist, "application/x-mpegURL");
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
