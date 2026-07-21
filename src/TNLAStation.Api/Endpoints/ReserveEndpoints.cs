using Microsoft.AspNetCore.Mvc;
using TNLAStation.Api.Contracts;
using TNLAStation.Application.Abstractions;
using TNLAStation.Application.Models;
using TNLAStation.Domain;

namespace TNLAStation.Api.Endpoints;

/// <summary>
/// 予約 1 件ごとの操作。ルールが作った予約は消しても作り直されるので、録らないという
/// 意思は skip で表す。
/// </summary>
internal static class ReserveEndpoints
{
    public static IEndpointRouteBuilder MapReserveEndpoints(this IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder reserves = endpoints.MapGroup("/api/reserves");

        reserves.MapGet("/lists", GetReserveListsAsync)
            .WithName("GetReserveLists")
            .WithSummary("予約一覧取得")
            .WithTags("reserves")
            .Produces<ReserveListsResponse>();

        reserves.MapGet("/{reserveId:long}", GetReserveAsync)
            .WithName("GetReserve")
            .WithSummary("予約情報取得")
            .WithTags("reserves")
            .Produces<ReserveItemResponse>()
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound);

        reserves.MapDelete("/{reserveId:long}", DeleteReserveAsync)
            .WithName("DeleteReserve")
            .WithSummary("予約削除")
            .WithTags("reserves")
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound);

        reserves.MapPut("/{reserveId:long}", UpdateReserveAsync)
            .WithName("UpdateReserve")
            .WithSummary("手動予約更新")
            .WithTags("reserves")
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound);

        reserves.MapDelete("/{reserveId:long}/overlap", CancelOverlapAsync)
            .WithName("CancelReserveOverlap")
            .WithSummary("予約の重複状態を解除")
            .WithTags("reserves")
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound);

        reserves.MapDelete("/{reserveId:long}/skip", CancelSkipAsync)
            .WithName("CancelReserveSkip")
            .WithSummary("予約の除外状態を解除")
            .WithTags("reserves")
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound);

        reserves.MapPost("/update", UpdateReservesAsync)
            .WithName("UpdateReserves")
            .WithSummary("予約の再生成")
            .WithTags("reserves");

        return endpoints;
    }

    /// <summary>
    /// 時間帯で区切った予約の一覧。番組表の画面が、どの枠が埋まっているかを引くのに使う。
    /// </summary>
    private static async Task<IResult> GetReserveListsAsync(
        IReserveRepository repository,
        [FromQuery] bool isHalfWidth,
        [FromQuery] long startAt,
        [FromQuery] long endAt,
        CancellationToken cancellationToken = default)
    {
        Page<Reservation> page = await repository.ListAsync(
            new ReserveQuery(isHalfWidth, Offset: 0, Limit: int.MaxValue),
            cancellationToken);

        // 期間は必須。範囲を絞らずに全部返すと、番組表を開くたびに予約を全件読むことになる。
        Reservation[] filtered = [.. page.Items
            .Where(reserve => reserve.EndAt > startAt && reserve.StartAt < endAt)];
        return Results.Ok(new ReserveListsResponse(
            [.. filtered.Where(IsNormal).Select(reserve => reserve.ToListItemResponse())],
            [.. filtered.Where(reserve => reserve.IsConflict).Select(reserve => reserve.ToListItemResponse())],
            [.. filtered.Where(reserve => reserve.IsSkip).Select(reserve => reserve.ToListItemResponse())],
            [.. filtered.Where(reserve => reserve.IsOverlap).Select(reserve => reserve.ToListItemResponse())]));
    }

    private static async Task<IResult> GetReserveAsync(
        long reserveId,
        IReserveRepository repository,
        [FromQuery] bool isHalfWidth,
        CancellationToken cancellationToken = default)
    {
        Reservation? reserve = await repository.GetAsync(reserveId, cancellationToken);
        return reserve is null ? NotFound() : Results.Ok(reserve.ToResponse(isHalfWidth));
    }

    /// <summary>
    /// 予約を消す。手動予約は消えるが、ルールが作った予約は消してもすぐ作り直されるので、
    /// 代わりに除外へ倒す。これは上流と同じ振る舞いで、画面の「削除」は両方を兼ねている。
    /// </summary>
    private static async Task<IResult> DeleteReserveAsync(
        long reserveId,
        IReserveRepository repository,
        CancellationToken cancellationToken)
    {
        return await repository.DeleteAsync(reserveId, cancellationToken)
            ? Results.NoContent()
            : NotFound();
    }

    private static async Task<IResult> CancelSkipAsync(
        long reserveId,
        IReserveRepository repository,
        CancellationToken cancellationToken)
    {
        return await repository.SetSkipAsync(reserveId, isSkip: false, cancellationToken)
            ? Results.NoContent()
            : NotFound();
    }

    private static async Task<IResult> CancelOverlapAsync(
        long reserveId,
        IReserveRepository repository,
        CancellationToken cancellationToken)
    {
        return await repository.ClearOverlapAsync(reserveId, cancellationToken)
            ? Results.NoContent()
            : NotFound();
    }

    private static async Task<IResult> UpdateReserveAsync(
        long reserveId,
        EditReserveRequest request,
        IReserveRepository repository,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return await repository.UpdateAsync(reserveId, request.ToCommand(), cancellationToken)
            ? Results.NoContent()
            : NotFound();
    }

    private static async Task<IResult> UpdateReservesAsync(
        IReserveGenerationTrigger trigger,
        CancellationToken cancellationToken)
    {
        await trigger.RequestAsync(cancellationToken);
        return Results.NoContent();
    }

    private static bool IsNormal(Reservation reserve) =>
        !reserve.IsConflict && !reserve.IsSkip && !reserve.IsOverlap;

    private static IResult NotFound() => Results.Json(
        new ErrorResponse(StatusCodes.Status404NotFound, "reserve is not found"),
        statusCode: StatusCodes.Status404NotFound);
}
