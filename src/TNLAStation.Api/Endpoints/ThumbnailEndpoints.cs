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

        thumbnails.MapGet("/{thumbnailId:long}", GetThumbnailAsync)
            .WithName("GetThumbnail")
            .WithSummary("サムネイル取得")
            .WithTags("thumbnails");

        thumbnails.MapPost("/", CreateMissingAsync)
            .WithName("CreateThumbnails")
            .WithSummary("サムネイルの一括作成")
            .WithTags("thumbnails");

        thumbnails.MapPost("/videos/{videoFileId:long}", CreateForVideoAsync)
            .WithName("CreateThumbnailForVideo")
            .WithSummary("サムネイル作成")
            .WithTags("thumbnails");

        thumbnails.MapPost("/cleanup", CleanupAsync)
            .WithName("CleanupThumbnails")
            .WithSummary("不要なサムネイルの削除")
            .WithTags("thumbnails");

        thumbnails.MapDelete("/{thumbnailId:long}", DeleteThumbnailAsync)
            .WithName("DeleteThumbnail")
            .WithSummary("サムネイル削除")
            .WithTags("thumbnails");

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
        long? id = await thumbnails.CreateForVideoFileAsync(videoFileId, cancellationToken);
        return id is null ? NotFound() : Results.Created($"/api/thumbnails/{id}", new AddedThumbnailResponse(id.Value));
    }

    private static async Task<IResult> CreateMissingAsync(
        IThumbnailService thumbnails,
        CancellationToken cancellationToken)
    {
        await thumbnails.CreateMissingAsync(cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> CleanupAsync(
        IThumbnailService thumbnails,
        CancellationToken cancellationToken)
    {
        await thumbnails.CleanupAsync(cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> DeleteThumbnailAsync(
        long thumbnailId,
        IThumbnailService thumbnails,
        CancellationToken cancellationToken)
    {
        return await thumbnails.DeleteAsync(thumbnailId, cancellationToken) ? Results.NoContent() : NotFound();
    }

    private static IResult NotFound() => Results.Json(
        new ErrorResponse(StatusCodes.Status404NotFound, "thumbnail is not found"),
        statusCode: StatusCodes.Status404NotFound);
}
