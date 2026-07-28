using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;
using TNLAStation.Api.Contracts;
using TNLAStation.Application.Abstractions;
using TNLAStation.Domain;

namespace TNLAStation.Api.Endpoints;

/// <summary>
/// 保存した動画ファイルを配る。録画そのものと違い、ここはファイルの話しかしない。
/// </summary>
internal static class VideoEndpoints
{
    public static IEndpointRouteBuilder MapVideoEndpoints(this IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder videos = endpoints.MapGroup("/api/videos");

        videos.MapGet("/{videoFileId}", GetVideoAsync)
            .WithName("GetVideoFile")
            .WithSummary("ビデオファイル")
            .WithTags("videos");

        videos.MapGet("/{videoFileId}/duration", GetDurationAsync)
            .WithName("GetVideoDuration")
            .WithSummary("動画の長さ")
            .WithTags("videos")
            .Produces<VideoDurationResponse>();

        videos.MapGet("/{videoFileId}/playlist", GetPlaylistAsync)
            .WithName("GetVideoPlaylist")
            .WithSummary("ビデオプレイリスト")
            .WithTags("videos");

        videos.MapPost("/upload", UploadAsync)
            .WithName("UploadVideoFile")
            .WithSummary("ビデオファイルのアップロード")
            .WithTags("videos")
            .DisableAntiforgery();

        videos.MapPost("/{videoFileId}/kodi", SendToKodiAsync)
            .WithName("SendVideoFileToKodi")
            .WithSummary("ビデオリンクを kodi へ送信")
            .WithTags("videos")
            .Produces<ResultCodeResponse>()
            .Produces<ErrorResponse>(StatusCodes.Status500InternalServerError);

        videos.MapDelete("/{videoFileId}", DeleteVideoAsync)
            .WithName("DeleteVideoFile")
            .WithSummary("ビデオファイル削除")
            .WithTags("videos")
            .Produces<ResultCodeResponse>()
            .Produces<ErrorResponse>(StatusCodes.Status500InternalServerError);

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
        // 上流 (VideoApiModel.getDuration) はここを 404 ではなく VideoFileIsUndefined 例外にして
        // おり、汎用の 500 として返る (ファイル取得 GET の 404 とは扱いが違う)。
        VideoFileLocation? file = await repository.GetAsync(videoFileId, cancellationToken);
        if (file is null || !File.Exists(file.FullPath))
        {
            throw new InvalidOperationException("VideoFileIsUndefined");
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
            return PlaylistNotFound();
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

    /// <summary>
    /// 外で作った動画を録画へ結び付ける。中身は検査しない。再生できるかどうかは、
    /// 実際に再生するときに分かる。
    /// </summary>
    private static async Task<IResult> UploadAsync(
        HttpRequest request,
        IVideoFileUploadRepository repository,
        CancellationToken cancellationToken)
    {
        if (!request.HasFormContentType)
        {
            return Results.BadRequest(new ValidationErrorResponse("multipart/form-data is required"));
        }

        IFormCollection form = await request.ReadFormAsync(cancellationToken);
        IFormFile? file = form.Files["file"];
        if (file is null)
        {
            // 上流 (videos/upload の post) はファイルが無いと FileIsNotFound を投げて 500 になる。
            throw new InvalidOperationException("FileIsNotFound");
        }

        string parentDirectoryName = form["parentDirectoryName"].ToString();
        string viewName = form["viewName"].ToString();
        string fileType = form["fileType"].ToString();
        string? subDirectory = form["subDirectory"].ToString() is { Length: > 0 } value ? value : null;

        // recordedId が数値ですらない・対応する録画が無い、のどちらも上流では区別されず
        // RecordedIdIsNull になる (recordedDB.findId が null を返す先と同じ経路)。
        long? id = long.TryParse(form["recordedId"], CultureInfo.InvariantCulture, out long recordedId)
            ? await UploadFileAsync(repository, recordedId, file, parentDirectoryName, viewName, fileType, subDirectory, cancellationToken)
            : null;

        if (id is null)
        {
            throw new InvalidOperationException("RecordedIdIsNull");
        }

        return Results.Ok(new UploadResultResponse(StatusCodes.Status200OK, "ok"));
    }

    private static async Task<long?> UploadFileAsync(
        IVideoFileUploadRepository repository,
        long recordedId,
        IFormFile file,
        string parentDirectoryName,
        string viewName,
        string fileType,
        string? subDirectory,
        CancellationToken cancellationToken)
    {
        await using Stream content = file.OpenReadStream();
        return await repository.UploadAsync(
            new VideoFileUpload(
                recordedId,
                string.IsNullOrWhiteSpace(viewName) ? Path.GetFileNameWithoutExtension(file.FileName) : viewName,
                file.FileName,
                parentDirectoryName,
                subDirectory,
                string.IsNullOrWhiteSpace(fileType) ? "encoded" : fileType),
            content,
            cancellationToken);
    }

    /// <summary>
    /// 手元ではなくテレビで再生させる。送るのは URL だけなので、Kodi から見て取りに
    /// 行ける宛先である必要がある。localhost では届かない。
    /// </summary>
    private static async Task<IResult> SendToKodiAsync(
        long videoFileId,
        SendVideoLinkToKodiRequest request,
        HttpContext context,
        IVideoFileRepository repository,
        IKodiClient kodi,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // 上流 (VideoApiModel.sendToKodi) は kodi 送り先名と動画の存在チェックをしており、
        // 無ければそれぞれ KodiHostIsUndefined / VideoFileIsUndefined を投げて 500 になる。
        if (!kodi.HostNames.Contains(request.KodiName, StringComparer.Ordinal))
        {
            throw new InvalidOperationException("KodiHostIsUndefined");
        }

        VideoFileLocation? file = await repository.GetAsync(videoFileId, cancellationToken);
        if (file is null)
        {
            throw new InvalidOperationException("VideoFileIsUndefined");
        }

        HttpRequest incoming = context.Request;
        string origin = kodi.PublicBaseUrl ?? $"{incoming.Scheme}://{incoming.Host}";
        string url = $"{origin.TrimEnd('/')}/api/videos/{videoFileId.ToString(CultureInfo.InvariantCulture)}";
        await kodi.PlayAsync(request.KodiName, url, cancellationToken);
        return Results.Ok(new ResultCodeResponse(StatusCodes.Status200OK));
    }

    private static async Task<IResult> DeleteVideoAsync(
        long videoFileId,
        IVideoFileRepository repository,
        IRecordedItemRepository recordedRepository,
        CancellationToken cancellationToken)
    {
        // 上流 (deleteVideoFile) はここで存在チェックとプロテクトチェックをしており、無ければ
        // VideoFileIsNotFound、プロテクト中なら RecordedIsProtected を投げて 500 になる。
        VideoFileLocation? file = await repository.GetAsync(videoFileId, cancellationToken);
        if (file is null)
        {
            throw new InvalidOperationException("VideoFileIsNotFound");
        }

        RecordedProgram? recorded = await recordedRepository.GetAsync(file.RecordedId, cancellationToken);
        if (recorded is { IsProtected: true })
        {
            throw new InvalidOperationException("RecordedIsProtected");
        }

        await repository.DeleteAsync(videoFileId, cancellationToken);
        return Results.Ok(new ResultCodeResponse(StatusCodes.Status200OK));
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

    private static IResult PlaylistNotFound() => Results.Json(
        new ErrorResponse(StatusCodes.Status404NotFound, "play list is not found"),
        statusCode: StatusCodes.Status404NotFound);
}
