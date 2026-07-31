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

        recorded.MapGet("/{recordedId}", GetRecordedAsync)
            .WithName("GetRecordedItem")
            .WithSummary("録画情報取得")
            .WithTags("recorded")
            .Produces<RecordedItemResponse>()
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound);

        recorded.MapDelete("/{recordedId}", DeleteRecordedAsync)
            .WithName("DeleteRecordedItem")
            .WithSummary("録画削除")
            .WithTags("recorded")
            .Produces<ResultCodeResponse>()
            .Produces<ErrorResponse>(StatusCodes.Status500InternalServerError);

        recorded.MapPut("/{recordedId}/protect", ProtectAsync)
            .WithName("ProtectRecorded")
            .WithSummary("録画の自動削除を防ぐ")
            .WithTags("recorded")
            .Produces<ResultCodeResponse>();

        recorded.MapPut("/{recordedId}/unprotect", UnprotectAsync)
            .WithName("UnprotectRecorded")
            .WithSummary("録画の自動削除の防止を解除")
            .WithTags("recorded")
            .Produces<ResultCodeResponse>();

        recorded.MapPost("/cleanup", CleanupAsync)
            .WithName("CleanupRecorded")
            .WithSummary("録画をクリーンアップ")
            .WithTags("recorded")
            .Produces<ResultCodeResponse>()
            .Produces<ErrorResponse>(StatusCodes.Status500InternalServerError);

        RouteGroupBuilder tags = endpoints.MapGroup("/api/tags");

        tags.MapPost("/", AddTagAsync)
            .WithName("AddRecordedTag")
            .WithSummary("録画タグ追加")
            .WithTags("tags")
            .Produces<AddedRecordedTagResponse>(StatusCodes.Status201Created);

        tags.MapPut("/{tagId}", UpdateTagAsync)
            .WithName("UpdateRecordedTag")
            .WithSummary("録画タグ更新")
            .WithTags("tags")
            .Produces<ResultCodeResponse>();

        tags.MapDelete("/{tagId}", DeleteTagAsync)
            .WithName("DeleteRecordedTag")
            .WithSummary("録画タグ削除")
            .WithTags("tags")
            .Produces<ResultCodeResponse>();

        tags.MapPut("/{tagId}/relate", AttachTagAsync)
            .WithName("AttachRecordedTag")
            .WithSummary("録画へタグを付ける")
            .WithTags("tags")
            .Produces<ResultCodeResponse>();

        tags.MapDelete("/{tagId}/relate", DetachTagAsync)
            .WithName("DetachRecordedTag")
            .WithSummary("録画からタグを外す")
            .WithTags("tags")
            .Produces<ResultCodeResponse>();

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

    /// <summary>
    /// 録画を消す。EPGStation は録画中でも「消す」で受け付け、まず録画を止めてから消す
    /// (画面の「削除」ボタンは録画中・録画済みの両方をこれ 1 本で扱う)。
    /// 存在チェックはせず、常に 200 で答える。
    /// </summary>
    private static async Task<IResult> DeleteRecordedAsync(
        long recordedId,
        IRecordedItemRepository repository,
        IRecordingStopService stopService,
        CancellationToken cancellationToken)
    {
        // EPGStation (RecordedManageModel.delete) はここで存在チェックとプロテクトチェックをしており、
        // 無ければ RecordedIdIsNotFound、プロテクト中なら RecordedIsProtected を投げて 500 になる。
        RecordedProgram? existing = await repository.GetAsync(recordedId, cancellationToken);
        if (existing is null)
        {
            throw new InvalidOperationException("RecordedIdIsNotFound");
        }

        if (existing.IsProtected)
        {
            throw new InvalidOperationException("RecordedIsProtected");
        }

        if (existing.IsRecording)
        {
            await stopService.StopAsync(recordedId, cancellationToken);
        }

        await repository.DeleteAsync(recordedId, cancellationToken);
        return Results.Ok(new ResultCodeResponse(StatusCodes.Status200OK));
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
        if (await repository.GetAsync(recordedId, cancellationToken) is null)
        {
            throw new InvalidOperationException("RecordedIsNull");
        }

        await repository.SetProtectedAsync(recordedId, isProtected, cancellationToken);
        return Results.Ok(new ResultCodeResponse(StatusCodes.Status200OK));
    }

    /// <summary>
    /// ファイルが無くなった録画を消す。外からファイルを消したときに、再生できない録画が
    /// 一覧に残り続けるのを片付ける。
    /// </summary>
    private static async Task<IResult> CleanupAsync(
        IRecordedItemRepository repository,
        CancellationToken cancellationToken)
    {
        // EPGStation は片付いた件数を返さず { code: 200 } だけを返す (recorded/cleanup.ts)。
        // 件数を足すと互換クライアントから見て未知の鍵が増えるので、内部の結果は捨てる。
        _ = await repository.CleanupAsync(cancellationToken);
        return Results.Ok(new ResultCodeResponse(StatusCodes.Status200OK));
    }

    private static async Task<IResult> AddTagAsync(
        RecordedTagRequest request,
        IRecordedTagWriteRepository repository,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        long id = await repository.AddTagAsync(request.Name, request.Color, cancellationToken);
        return Results.Json(new AddedRecordedTagResponse(id), statusCode: StatusCodes.Status201Created);
    }

    private static async Task<IResult> UpdateTagAsync(
        long tagId,
        RecordedTagRequest request,
        IRecordedTagWriteRepository repository,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        await repository.UpdateTagAsync(tagId, request.Name, request.Color, cancellationToken);
        return Results.Ok(new ResultCodeResponse(StatusCodes.Status200OK));
    }

    private static async Task<IResult> DeleteTagAsync(
        long tagId,
        IRecordedTagWriteRepository repository,
        CancellationToken cancellationToken)
    {
        await repository.DeleteTagAsync(tagId, cancellationToken);
        return Results.Ok(new ResultCodeResponse(StatusCodes.Status200OK));
    }

    private static async Task<IResult> AttachTagAsync(
        long tagId,
        RelateRecordedTagRequest request,
        IRecordedTagWriteRepository repository,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        await repository.SetTagAsync(request.RecordedId, tagId, attached: true, cancellationToken);
        return Results.Ok(new ResultCodeResponse(StatusCodes.Status200OK));
    }

    private static async Task<IResult> DetachTagAsync(
        long tagId,
        [FromQuery] long? recordedId,
        IRecordedTagWriteRepository repository,
        CancellationToken cancellationToken)
    {
        if (recordedId is null)
        {
            throw new InvalidOperationException("RecordedIsUndefined");
        }

        await repository.SetTagAsync(recordedId.Value, tagId, attached: false, cancellationToken);
        return Results.Ok(new ResultCodeResponse(StatusCodes.Status200OK));
    }

    private static IResult NotFound(string message = "recorded is not Found") => Results.Json(
        new ErrorResponse(StatusCodes.Status404NotFound, message),
        statusCode: StatusCodes.Status404NotFound);
}
