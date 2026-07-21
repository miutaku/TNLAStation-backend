using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;
using TNLAStation.Api.Contracts;
using TNLAStation.Application.Abstractions;

namespace TNLAStation.Api.Endpoints;

/// <summary>
/// 保存した動画ファイルを配る。録画そのものと違い、ここはファイルの話しかしない。
/// </summary>
internal static class VideoEndpoints
{
    public static IEndpointRouteBuilder MapVideoEndpoints(this IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder videos = endpoints.MapGroup("/api/videos");

        videos.MapGet("/{videoFileId:long}", GetVideoAsync)
            .WithName("GetVideoFile")
            .WithSummary("ビデオファイル")
            .WithTags("videos");

        videos.MapGet("/{videoFileId:long}/duration", GetDurationAsync)
            .WithName("GetVideoDuration")
            .WithSummary("動画の長さ")
            .WithTags("videos")
            .Produces<VideoDurationResponse>();

        videos.MapGet("/{videoFileId:long}/playlist", GetPlaylistAsync)
            .WithName("GetVideoPlaylist")
            .WithSummary("ビデオプレイリスト")
            .WithTags("videos");

        videos.MapDelete("/{videoFileId:long}", DeleteVideoAsync)
            .WithName("DeleteVideoFile")
            .WithSummary("ビデオファイル削除")
            .WithTags("videos");

        return endpoints;
    }

    private static async Task<IResult> GetVideoAsync(
        long videoFileId,
        IVideoFileRepository repository,
        [FromQuery] bool? isDownload = null,
        CancellationToken cancellationToken = default)
    {
        VideoFileLocation? file = await repository.GetAsync(videoFileId, cancellationToken);
        if (file is null || !File.Exists(file.FullPath))
        {
            return NotFound();
        }

        // 範囲指定を受け付ける。受け付けないと、再生位置を動かすたびに先頭から読み直しになる。
        return Results.File(
            File.OpenRead(file.FullPath),
            ContentTypeFor(file.Filename),
            fileDownloadName: isDownload == true ? Path.GetFileName(file.Filename) : null,
            enableRangeProcessing: true);
    }

    private static async Task<IResult> GetDurationAsync(
        long videoFileId,
        IVideoFileRepository repository,
        IMediaProbe probe,
        CancellationToken cancellationToken)
    {
        VideoFileLocation? file = await repository.GetAsync(videoFileId, cancellationToken);
        if (file is null || !File.Exists(file.FullPath))
        {
            return NotFound();
        }

        double? duration = await probe.GetDurationSecondsAsync(file.FullPath, cancellationToken);
        return duration is null
            ? Results.Json(
                new ErrorResponse(StatusCodes.Status500InternalServerError, "duration is not available"),
                statusCode: StatusCodes.Status500InternalServerError)
            : Results.Ok(new VideoDurationResponse(duration.Value));
    }

    /// <summary>
    /// 外部の再生機へ渡すためのプレイリスト。中身は 1 本だけを指す。再生機によっては
    /// 動画の URL を直接渡せないので、この 1 枚を挟む。
    /// </summary>
    private static async Task<IResult> GetPlaylistAsync(
        long videoFileId,
        HttpContext context,
        IVideoFileRepository repository,
        CancellationToken cancellationToken)
    {
        VideoFileLocation? file = await repository.GetAsync(videoFileId, cancellationToken);
        if (file is null)
        {
            return NotFound();
        }

        HttpRequest request = context.Request;
        string origin = $"{request.Scheme}://{request.Host}";
        string playlist = string.Join(
            '\n',
            "#EXTM3U",
            $"#EXTINF:-1,{file.Name}",
            $"{origin}/api/videos/{videoFileId.ToString(CultureInfo.InvariantCulture)}",
            string.Empty);

        return Results.Text(playlist, "application/x-mpegURL");
    }

    private static async Task<IResult> DeleteVideoAsync(
        long videoFileId,
        IVideoFileRepository repository,
        CancellationToken cancellationToken)
    {
        return await repository.DeleteAsync(videoFileId, cancellationToken)
            ? Results.NoContent()
            : NotFound();
    }

    private static string ContentTypeFor(string filename) =>
        Path.GetExtension(filename).ToLowerInvariant() switch
        {
            ".ts" or ".m2ts" => "video/mp2t",
            ".mp4" => "video/mp4",
            ".webm" => "video/webm",
            ".mkv" => "video/x-matroska",
            _ => "application/octet-stream",
        };

    private static IResult NotFound() => Results.Json(
        new ErrorResponse(StatusCodes.Status404NotFound, "video file is not found"),
        statusCode: StatusCodes.Status404NotFound);
}
