using Microsoft.Extensions.Options;
using TNLAStation.FfmpegWorker.Contracts;
using TNLAStation.FfmpegWorker.Options;
using TNLAStation.FfmpegWorker.Streaming;

namespace TNLAStation.FfmpegWorker.Endpoints;

public static class StreamEndpoints
{
    public static void MapStreamEndpoints(this WebApplication app)
    {
        app.MapPost("/streams/hls/live", async (
            HlsLiveStartRequest request,
            HlsSessionRegistry registry,
            IOptions<FfmpegOptions> options,
            CancellationToken cancellationToken) =>
        {
            await registry.StartLiveAsync(
                request.StreamId,
                request.ChannelId,
                request.Height,
                request.VideoBitrate,
                request.AudioBitrate,
                request.SegmentSeconds,
                request.Priority,
                request.Command,
                cancellationToken);
            return Results.Accepted(value: new HlsStartResponse(options.Value.PublicBaseUrl));
        });

        app.MapPost("/streams/lowlatency/live", async (
            LowLatencyLiveStartRequest request,
            HlsSessionRegistry registry,
            IOptions<FfmpegOptions> options,
            CancellationToken cancellationToken) =>
        {
            await registry.StartLowLatencyAsync(
                request.StreamId,
                request.ChannelId,
                request.Height,
                request.VideoBitrate,
                request.AudioBitrate,
                request.Priority,
                cancellationToken);
            return Results.Accepted(value: new HlsStartResponse(options.Value.PublicBaseUrl));
        });

        app.MapPost("/streams/hls/recorded", async (
            HlsRecordedStartRequest request,
            HlsSessionRegistry registry,
            IOptions<FfmpegOptions> options) =>
        {
            await registry.StartRecordedAsync(
                request.StreamId,
                request.Path,
                request.Height,
                request.VideoBitrate,
                request.AudioBitrate,
                request.SegmentSeconds,
                request.PlayPosition,
                request.Command,
                request.IsTransportStream);
            return Results.Accepted(value: new HlsStartResponse(options.Value.PublicBaseUrl));
        });

        app.MapGet("/streams/hls/{streamId:long}", (long streamId, HlsSessionRegistry registry) =>
        {
            (bool found, bool isRunning, string? lastError, string? recentOutput) = registry.GetStatus(streamId);
            return Results.Ok(new HlsStatusResponse(found, isRunning, lastError, recentOutput));
        });

        app.MapDelete("/streams/hls/{streamId:long}", async (long streamId, HlsSessionRegistry registry) =>
        {
            bool stopped = await registry.StopAsync(streamId);
            return stopped ? Results.NoContent() : Results.NotFound();
        });

        app.MapPost("/streams/transcode/live", async (TranscodeLiveRequest request, TranscodeStreamer streamer, CancellationToken cancellationToken) =>
        {
            Stream stream = await streamer.OpenLiveAsync(
                request.ChannelId,
                request.Height,
                request.VideoBitrate,
                request.AudioBitrate,
                request.FormatArguments,
                request.Priority,
                request.Command,
                cancellationToken);
            return Results.Stream(stream, "application/octet-stream");
        });

        app.MapPost("/streams/transcode/recorded", async (TranscodeRecordedRequest request, TranscodeStreamer streamer) =>
        {
            Stream stream = await streamer.OpenRecordedAsync(
                request.Path,
                request.Height,
                request.VideoBitrate,
                request.AudioBitrate,
                request.FormatArguments,
                request.PlayPosition,
                request.Command,
                request.IsTransportStream);
            return Results.Stream(stream, "application/octet-stream");
        });
    }
}
