using TNLAStation.Api.Contracts;
using TNLAStation.Application.Abstractions;

namespace TNLAStation.Api.Endpoints;

/// <summary>
/// 録画のサムネイル。一覧で中身を思い出すためのもので、無くても録画は成立する。
/// </summary>
internal static class ThumbnailEndpoints
{
    public static IEndpointRouteBuilder MapThumbnailEndpoints(this IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder thumbnails = endpoints.MapGroup("/api/thumbnails");

        thumbnails.MapGet("/{thumbnailId}", GetThumbnailAsync)
            .WithName("GetThumbnail")
            .WithSummary("サムネイル取得")
            .WithTags("thumbnails");

        thumbnails.MapPost("/", CreateMissingAsync)
            .WithName("CreateThumbnails")
            .WithSummary("サムネイルの一括作成")
            .WithTags("thumbnails")
            .Produces<ResultCodeResponse>();

        thumbnails.MapPost("/videos/{videoFileId}", CreateForVideoAsync)
            .WithName("CreateThumbnailForVideo")
            .WithSummary("サムネイル作成")
            .WithTags("thumbnails")
            .Produces<ResultCodeResponse>();

        thumbnails.MapPost("/cleanup", CleanupAsync)
            .WithName("CleanupThumbnails")
            .WithSummary("不要なサムネイルの削除")
            .WithTags("thumbnails")
            .Produces<ResultCodeResponse>();

        thumbnails.MapDelete("/{thumbnailId}", DeleteThumbnailAsync)
            .WithName("DeleteThumbnail")
            .WithSummary("サムネイル削除")
            .WithTags("thumbnails")
            .Produces<ResultCodeResponse>()
            .Produces<ErrorResponse>(StatusCodes.Status500InternalServerError);

        return endpoints;
    }

    private static async Task<IResult> GetThumbnailAsync(
        long thumbnailId,
        IThumbnailService thumbnails,
        CancellationToken cancellationToken)
    {
        ThumbnailFile? thumbnail = await thumbnails.GetAsync(thumbnailId, cancellationToken);
        return thumbnail is null || !File.Exists(thumbnail.FullPath)
            ? NotFound()
            : Results.File(File.OpenRead(thumbnail.FullPath), "image/jpeg");
    }

    private static async Task<IResult> CreateForVideoAsync(
        long videoFileId,
        IThumbnailService thumbnails,
        CancellationToken cancellationToken)
    {
        // EPGStation は存在チェックをせず、常に 200 を返す。
        await thumbnails.CreateForVideoFileAsync(videoFileId, cancellationToken);
        return Results.Ok(new ResultCodeResponse(StatusCodes.Status200OK));
    }

    private static async Task<IResult> CreateMissingAsync(
        IThumbnailService thumbnails,
        CancellationToken cancellationToken)
    {
        await thumbnails.CreateMissingAsync(cancellationToken);
        return Results.Ok(new ResultCodeResponse(StatusCodes.Status200OK));
    }

    private static async Task<IResult> CleanupAsync(
        IThumbnailService thumbnails,
        CancellationToken cancellationToken)
    {
        await thumbnails.CleanupAsync(cancellationToken);
        return Results.Ok(new ResultCodeResponse(StatusCodes.Status200OK));
    }

    private static async Task<IResult> DeleteThumbnailAsync(
        long thumbnailId,
        IThumbnailService thumbnails,
        CancellationToken cancellationToken)
    {
        // EPGStation (ThumbnailManageModel.delete) はここで存在チェックをしており、無ければ
        // ThumbnailIsNotFound を投げて 500 になる。
        ThumbnailFile? existing = await thumbnails.GetAsync(thumbnailId, cancellationToken);
        if (existing is null)
        {
            throw new InvalidOperationException("ThumbnailIsNotFound");
        }

        await thumbnails.DeleteAsync(thumbnailId, cancellationToken);
        return Results.Ok(new ResultCodeResponse(StatusCodes.Status200OK));
    }

    private static IResult NotFound() => Results.Json(
        new ErrorResponse(StatusCodes.Status404NotFound, "thumbnail is not Found"),
        statusCode: StatusCodes.Status404NotFound);
}
