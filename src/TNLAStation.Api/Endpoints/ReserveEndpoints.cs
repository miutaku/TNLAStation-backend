using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using TNLAStation.Api.Contracts;
using TNLAStation.Application.Abstractions;
using TNLAStation.Application.Models;
using TNLAStation.Domain;
using TNLAStation.Infrastructure.Configuration;

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

        reserves.MapGet("/{reserveId}", GetReserveAsync)
            .WithName("GetReserve")
            .WithSummary("予約情報取得")
            .WithTags("reserves")
            .Produces<ReserveItemResponse>()
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound);

        reserves.MapDelete("/{reserveId}", DeleteReserveAsync)
            .WithName("DeleteReserve")
            .WithSummary("予約削除")
            .WithTags("reserves")
            .Produces<ErrorResponse>(StatusCodes.Status500InternalServerError);

        reserves.MapPut("/{reserveId}", UpdateReserveAsync)
            .WithName("UpdateReserve")
            .WithSummary("手動予約更新")
            .WithTags("reserves")
            .Produces<ResultCodeResponse>(StatusCodes.Status201Created)
            .Produces<ErrorResponse>(StatusCodes.Status500InternalServerError);

        reserves.MapDelete("/{reserveId}/overlap", CancelOverlapAsync)
            .WithName("CancelReserveOverlap")
            .WithSummary("予約の重複状態を解除")
            .WithTags("reserves")
            .Produces<ErrorResponse>(StatusCodes.Status500InternalServerError);

        reserves.MapDelete("/{reserveId}/skip", CancelSkipAsync)
            .WithName("CancelReserveSkip")
            .WithSummary("予約の除外状態を解除")
            .WithTags("reserves")
            .Produces<ErrorResponse>(StatusCodes.Status500InternalServerError);

        reserves.MapPost("/update", UpdateReservesAsync)
            .WithName("UpdateReserves")
            .WithSummary("予約の再生成")
            .WithTags("reserves")
            .Produces<ResultCodeResponse>();

        return endpoints;
    }

    /// <summary>
    /// 時間帯で区切った予約の一覧。番組表の画面が、どの枠が埋まっているかを引くのに使う。
    /// </summary>
    private static async Task<IResult> GetReserveListsAsync(
        IReserveRepository repository,
        [FromQuery] long startAt,
        [FromQuery] long endAt,
        CancellationToken cancellationToken = default)
    {
        Page<Reservation> page = await repository.ListAsync(
            new ReserveQuery(IsHalfWidth: false, Offset: 0, Limit: int.MaxValue),
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
    /// 代わりに除外へ倒す。これは EPGStation と同じ振る舞いで、画面の「削除」は両方を兼ねている。
    /// </summary>
    private static async Task<IResult> DeleteReserveAsync(
        long reserveId,
        IReserveRepository repository,
        IEpgRepository epg,
        ICommandHookRunner hooks,
        IOptions<CommandHookOptions> commandHookOptions,
        CancellationToken cancellationToken)
    {
        Reservation? reserve = await repository.GetAsync(reserveId, cancellationToken);
        if (reserve is null)
        {
            // EPGStation (cancel) はここで存在チェックをしており、無ければ ReservationIsNotFound を
            // 投げて 500 になる。空振りを 200 で許すのは skip/overlap 解除の側だけ。
            throw new InvalidOperationException("ReservationIsNotFound");
        }

        await repository.DeleteAsync(reserveId, cancellationToken);
        hooks.RunReserveHook(
            commandHookOptions.Value.ReserveDeletedCommand,
            await ReserveHookPayloads.BuildAsync(reserve, epg, cancellationToken));

        return Results.Ok();
    }

    private static async Task<IResult> CancelSkipAsync(
        long reserveId,
        IReserveRepository repository,
        IEpgRepository epg,
        ICommandHookRunner hooks,
        IOptions<CommandHookOptions> commandHookOptions,
        CancellationToken cancellationToken)
    {
        Reservation? reserve = await repository.GetAsync(reserveId, cancellationToken);
        if (reserve is null)
        {
            throw new InvalidOperationException("ReservationIsNotFound");
        }

        // ルール予約以外・そもそも除外されていない予約には何もしない (EPGStation と同じ)。
        if (reserve.RuleId is not null && reserve.IsSkip)
        {
            await repository.SetSkipAsync(reserveId, isSkip: false, cancellationToken);
            await FireUpdateHookAsync(reserveId, repository, epg, hooks, commandHookOptions, cancellationToken);
        }

        return Results.Ok();
    }

    private static async Task<IResult> CancelOverlapAsync(
        long reserveId,
        IReserveRepository repository,
        IEpgRepository epg,
        ICommandHookRunner hooks,
        IOptions<CommandHookOptions> commandHookOptions,
        CancellationToken cancellationToken)
    {
        Reservation? reserve = await repository.GetAsync(reserveId, cancellationToken);
        if (reserve is null)
        {
            throw new InvalidOperationException("ReservationIsNotFound");
        }

        // ルール予約以外・そもそも重複していない予約には何もしない (EPGStation と同じ)。
        if (reserve.RuleId is not null && reserve.IsOverlap)
        {
            await repository.ClearOverlapAsync(reserveId, cancellationToken);
            await FireUpdateHookAsync(reserveId, repository, epg, hooks, commandHookOptions, cancellationToken);
        }

        return Results.Ok();
    }

    private static async Task<IResult> UpdateReserveAsync(
        long reserveId,
        EditReserveRequest request,
        IReserveRepository repository,
        IEpgRepository epg,
        ICommandHookRunner hooks,
        IOptions<CommandHookOptions> commandHookOptions,
        IOptions<EncodeOptions> encodeOptions,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Reservation? existing = await repository.GetAsync(reserveId, cancellationToken);
        if (existing is null)
        {
            // EPGStation (edit) はここで存在チェックをしており、無ければ ReservationIsNotFound を
            // 投げて 500 になる。
            throw new InvalidOperationException("ReservationIsNotFound");
        }

        CreateReserveCommand command = request.ToCommand();

        // EPGStation (checkManualReserveOption) はここでもエンコードオプションを検査しており、
        // 落ちると ReservationEditError が汎用の 500 として返る。
        if (!EncodeOptionValidationPolicy.IsValid(
            command.Encode, [.. encodeOptions.Value.Modes.Select(mode => mode.Name)], encodeOptions.Value.Modes.Count > 0))
        {
            throw new InvalidOperationException("ReservationEditError");
        }

        // ルール予約の編集は、生成で行が置き換わっても失われないよう予約の安定キーへ保存する。
        await repository.UpdateAsync(reserveId, command, cancellationToken);

        Reservation? updated = await repository.GetAsync(reserveId, cancellationToken);
        if (updated is not null)
        {
            hooks.RunReserveHook(
                commandHookOptions.Value.ReserveUpdateCommand,
                await ReserveHookPayloads.BuildAsync(updated, epg, cancellationToken));
        }

        // EPGStation はここだけ 200 ではなく 201 + { code, message } で答える。
        return Results.Json(
            new ResultMessageResponse(StatusCodes.Status201Created, "ok"),
            statusCode: StatusCodes.Status201Created);
    }

    private static async Task<IResult> UpdateReservesAsync(
        IReserveGenerationTrigger trigger,
        CancellationToken cancellationToken)
    {
        await trigger.RequestAsync(cancellationToken);
        return Results.Ok(new ResultCodeResponse(StatusCodes.Status200OK));
    }

    private static bool IsNormal(Reservation reserve) =>
        !reserve.IsConflict && !reserve.IsSkip && !reserve.IsOverlap;

    /// <summary>skip/overlap の解除は予約そのものの内容を変えないので、更新フックだけ鳴らす。</summary>
    private static async Task FireUpdateHookAsync(
        long reserveId,
        IReserveRepository repository,
        IEpgRepository epg,
        ICommandHookRunner hooks,
        IOptions<CommandHookOptions> commandHookOptions,
        CancellationToken cancellationToken)
    {
        Reservation? reserve = await repository.GetAsync(reserveId, cancellationToken);
        if (reserve is not null)
        {
            hooks.RunReserveHook(
                commandHookOptions.Value.ReserveUpdateCommand,
                await ReserveHookPayloads.BuildAsync(reserve, epg, cancellationToken));
        }
    }

    private static IResult NotFound() => Results.Json(
        new ErrorResponse(StatusCodes.Status404NotFound, "reserve is not found"),
        statusCode: StatusCodes.Status404NotFound);
}
