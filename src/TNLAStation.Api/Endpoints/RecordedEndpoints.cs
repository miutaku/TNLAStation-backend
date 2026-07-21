using Microsoft.AspNetCore.Mvc;
using TNLAStation.Api.Contracts;
using TNLAStation.Application.Abstractions;
using TNLAStation.Domain;

namespace TNLAStation.Api.Endpoints;

/// <summary>
/// 録画 1 件ごとの操作と、録画へ付ける tag。
/// </summary>
internal static class RecordedEndpoints
{
    public static IEndpointRouteBuilder MapRecordedEndpoints(this IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder recorded = endpoints.MapGroup("/api/recorded");

        recorded.MapGet("/{recordedId:long}", GetRecordedAsync)
            .WithName("GetRecordedItem")
            .WithSummary("録画情報取得")
            .WithTags("recorded")
            .Produces<RecordedItemResponse>()
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound);

        recorded.MapDelete("/{recordedId:long}", DeleteRecordedAsync)
            .WithName("DeleteRecordedItem")
            .WithSummary("録画削除")
            .WithTags("recorded")
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound);

        recorded.MapPut("/{recordedId:long}/protect", ProtectAsync)
            .WithName("ProtectRecorded")
            .WithSummary("録画の自動削除を防ぐ")
            .WithTags("recorded")
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound);

        recorded.MapPut("/{recordedId:long}/unprotect", UnprotectAsync)
            .WithName("UnprotectRecorded")
            .WithSummary("録画の自動削除の防止を解除")
            .WithTags("recorded")
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound);

        recorded.MapPost("/cleanup", CleanupAsync)
            .WithName("CleanupRecorded")
            .WithSummary("実体の無い録画を片付ける")
            .WithTags("recorded");

        RouteGroupBuilder tags = endpoints.MapGroup("/api/tags");

        tags.MapPost("/", AddTagAsync)
            .WithName("AddRecordedTag")
            .WithSummary("録画タグ追加")
            .WithTags("tags")
            .Produces<AddedRecordedTagResponse>(StatusCodes.Status201Created);

        tags.MapPut("/{tagId:long}", UpdateTagAsync)
            .WithName("UpdateRecordedTag")
            .WithSummary("録画タグ更新")
            .WithTags("tags")
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound);

        tags.MapDelete("/{tagId:long}", DeleteTagAsync)
            .WithName("DeleteRecordedTag")
            .WithSummary("録画タグ削除")
            .WithTags("tags")
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound);

        tags.MapPut("/{tagId:long}/relate", AttachTagAsync)
            .WithName("AttachRecordedTag")
            .WithSummary("録画へタグを付ける")
            .WithTags("tags")
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound);

        tags.MapDelete("/{tagId:long}/relate", DetachTagAsync)
            .WithName("DetachRecordedTag")
            .WithSummary("録画からタグを外す")
            .WithTags("tags")
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound);

        return endpoints;
    }

    private static async Task<IResult> GetRecordedAsync(
        long recordedId,
        IRecordedItemRepository repository,
        [FromQuery] bool isHalfWidth,
        CancellationToken cancellationToken = default)
    {
        RecordedProgram? recorded = await repository.GetAsync(recordedId, cancellationToken);
        return recorded is null ? NotFound() : Results.Ok(recorded.ToResponse(isHalfWidth));
    }

    private static async Task<IResult> DeleteRecordedAsync(
        long recordedId,
        IRecordedItemRepository repository,
        CancellationToken cancellationToken)
    {
        return await repository.DeleteAsync(recordedId, cancellationToken) ? Results.NoContent() : NotFound();
    }

    private static Task<IResult> ProtectAsync(
        long recordedId,
        IRecordedItemRepository repository,
        CancellationToken cancellationToken) =>
        SetProtectedAsync(recordedId, isProtected: true, repository, cancellationToken);

    private static Task<IResult> UnprotectAsync(
        long recordedId,
        IRecordedItemRepository repository,
        CancellationToken cancellationToken) =>
        SetProtectedAsync(recordedId, isProtected: false, repository, cancellationToken);

    private static async Task<IResult> SetProtectedAsync(
        long recordedId,
        bool isProtected,
        IRecordedItemRepository repository,
        CancellationToken cancellationToken)
    {
        return await repository.SetProtectedAsync(recordedId, isProtected, cancellationToken)
            ? Results.NoContent()
            : NotFound();
    }

    /// <summary>
    /// ファイルが無くなった録画を消す。外からファイルを消したときに、再生できない録画が
    /// 一覧に残り続けるのを片付ける。
    /// </summary>
    private static async Task<IResult> CleanupAsync(
        IRecordedItemRepository repository,
        CancellationToken cancellationToken)
    {
        int removed = await repository.CleanupAsync(cancellationToken);
        return Results.Ok(new RecordedCleanupResponse(removed));
    }

    private static async Task<IResult> AddTagAsync(
        RecordedTagRequest request,
        IRecordedTagWriteRepository repository,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        long id = await repository.AddTagAsync(request.Name, request.Color, cancellationToken);
        return Results.Created($"/api/tags/{id}", new AddedRecordedTagResponse(id));
    }

    private static async Task<IResult> UpdateTagAsync(
        long tagId,
        RecordedTagRequest request,
        IRecordedTagWriteRepository repository,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return await repository.UpdateTagAsync(tagId, request.Name, request.Color, cancellationToken)
            ? Results.NoContent()
            : NotFound("tag is not found");
    }

    private static async Task<IResult> DeleteTagAsync(
        long tagId,
        IRecordedTagWriteRepository repository,
        CancellationToken cancellationToken)
    {
        return await repository.DeleteTagAsync(tagId, cancellationToken)
            ? Results.NoContent()
            : NotFound("tag is not found");
    }

    private static async Task<IResult> AttachTagAsync(
        long tagId,
        RelateRecordedTagRequest request,
        IRecordedTagWriteRepository repository,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return await repository.SetTagAsync(request.RecordedId, tagId, attached: true, cancellationToken)
            ? Results.NoContent()
            : NotFound("tag or recorded is not found");
    }

    private static async Task<IResult> DetachTagAsync(
        long tagId,
        [FromQuery] long recordedId,
        IRecordedTagWriteRepository repository,
        CancellationToken cancellationToken)
    {
        return await repository.SetTagAsync(recordedId, tagId, attached: false, cancellationToken)
            ? Results.NoContent()
            : NotFound("tag or recorded is not found");
    }

    private static IResult NotFound(string message = "recorded is not found") => Results.Json(
        new ErrorResponse(StatusCodes.Status404NotFound, message),
        statusCode: StatusCodes.Status404NotFound);
}
