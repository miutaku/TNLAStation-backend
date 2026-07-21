using Microsoft.AspNetCore.Mvc;
using TNLAStation.Api.Contracts;
using TNLAStation.Application.Abstractions;
using TNLAStation.Application.Models;

namespace TNLAStation.Api.Endpoints;

internal static class EpgStationEndpoints
{
    private static readonly HashSet<string> ReserveTypes =
        new(StringComparer.Ordinal) { "all", "normal", "conflict", "skip", "overlap" };

    public static IEndpointRouteBuilder MapEpgStationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder api = endpoints.MapGroup("/api");

        api.MapGet("/config", GetConfigAsync)
            .WithName("GetConfig")
            .WithSummary("config 情報取得")
            .WithDescription("EPGStation クライアント向け config 情報を取得する。")
            .WithTags("config")
            .Produces<ConfigResponse>(StatusCodes.Status200OK)
            .Produces<ErrorResponse>(StatusCodes.Status500InternalServerError);

        api.MapGet("/recorded", GetRecordedAsync)
            .WithName("GetRecorded")
            .WithSummary("録画情報取得")
            .WithDescription("録画済み番組を検索・ページングして取得する。")
            .WithTags("recorded")
            .Produces<RecordsResponse>(StatusCodes.Status200OK)
            .Produces<ValidationErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ErrorResponse>(StatusCodes.Status500InternalServerError);

        api.MapPost("/recorded", CreateRecordedAsync)
            .WithName("CreateRecorded")
            .WithSummary("録画番組情報の新規作成")
            .WithDescription("録画番組情報を新規作成する。")
            .WithTags("recorded")
            .Accepts<CreateRecordedRequest>("application/json")
            .Produces<CreatedRecordedResponse>(StatusCodes.Status201Created)
            .Produces<ValidationErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ErrorResponse>(StatusCodes.Status500InternalServerError);

        api.MapGet("/reserves", GetReservesAsync)
            .WithName("GetReserves")
            .WithSummary("予約情報取得")
            .WithDescription("予約情報を検索・ページングして取得する。")
            .WithTags("reserves")
            .Produces<ReservesResponse>(StatusCodes.Status200OK)
            .Produces<ValidationErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ErrorResponse>(StatusCodes.Status500InternalServerError);

        api.MapPost("/reserves", CreateReserveAsync)
            .WithName("CreateReserve")
            .WithSummary("予約追加")
            .WithDescription("手動予約を追加する。")
            .WithTags("reserves")
            .Accepts<CreateReserveRequest>("application/json")
            .Produces<AddedReserveResponse>(StatusCodes.Status201Created)
            .Produces<ValidationErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ErrorResponse>(StatusCodes.Status500InternalServerError);

        api.MapGet("/version", GetVersionAsync)
            .WithName("GetVersion")
            .WithSummary("バージョン情報取得")
            .WithDescription("EPGStation 互換バージョン情報を取得する。")
            .WithTags("version")
            .Produces<VersionResponse>(StatusCodes.Status200OK)
            .Produces<ErrorResponse>(StatusCodes.Status500InternalServerError);

        return endpoints;
    }

    private static async Task<IResult> GetConfigAsync(
        IConfigRepository repository,
        CancellationToken cancellationToken)
    {
        var config = await repository.GetAsync(cancellationToken);
        return Results.Ok(config.ToResponse());
    }

    private static async Task<IResult> GetRecordedAsync(
        IRecordedRepository repository,
        [FromQuery] bool isHalfWidth,
        [FromQuery] int offset = 0,
        [FromQuery] int limit = 24,
        [FromQuery] bool? isReverse = null,
        [FromQuery] long? ruleId = null,
        [FromQuery] long? channelId = null,
        [FromQuery] int? genre = null,
        [FromQuery] string? keyword = null,
        [FromQuery] bool? hasOriginalFile = null,
        CancellationToken cancellationToken = default)
    {
        if (offset < 0 || limit < 0)
        {
            return Results.BadRequest(new ValidationErrorResponse("offset and limit must be greater than or equal to 0"));
        }

        var query = new RecordedQuery(
            isHalfWidth,
            offset,
            limit,
            isReverse,
            ruleId,
            channelId,
            genre,
            keyword,
            hasOriginalFile);
        Page<TNLAStation.Domain.RecordedProgram> page =
            await repository.ListAsync(query, cancellationToken);
        var response = new RecordsResponse(
            page.Items.Select(item => item.ToResponse(isHalfWidth)).ToArray(),
            page.Total);

        return Results.Ok(response);
    }

    private static async Task<IResult> CreateRecordedAsync(
        CreateRecordedRequest request,
        IRecordedRepository repository,
        CancellationToken cancellationToken)
    {
        var command = new CreateRecordedCommand(
            request.ChannelId,
            request.StartAt,
            request.EndAt,
            request.Name,
            request.RuleId,
            request.Description,
            request.Extended,
            request.Genre1,
            request.SubGenre1,
            request.Genre2,
            request.SubGenre2,
            request.Genre3,
            request.SubGenre3);
        long id = await repository.AddAsync(command, cancellationToken);

        return Results.Json(
            new CreatedRecordedResponse(id),
            statusCode: StatusCodes.Status201Created);
    }

    private static async Task<IResult> GetReservesAsync(
        IReserveRepository repository,
        [FromQuery] bool isHalfWidth,
        [FromQuery] int offset = 0,
        [FromQuery] int limit = 24,
        [FromQuery] string? type = null,
        [FromQuery] long? ruleId = null,
        CancellationToken cancellationToken = default)
    {
        if (offset < 0 || limit < 0)
        {
            return Results.BadRequest(new ValidationErrorResponse("offset and limit must be greater than or equal to 0"));
        }

        if (type is not null && !ReserveTypes.Contains(type))
        {
            return Results.BadRequest(new ValidationErrorResponse("type must be all, normal, conflict, skip, or overlap"));
        }

        var query = new ReserveQuery(isHalfWidth, offset, limit, type, ruleId);
        Page<TNLAStation.Domain.Reservation> page = await repository.ListAsync(query, cancellationToken);
        var response = new ReservesResponse(
            page.Items.Select(item => item.ToResponse(isHalfWidth)).ToArray(),
            page.Total);

        return Results.Ok(response);
    }

    private static async Task<IResult> CreateReserveAsync(
        CreateReserveRequest request,
        IReserveRepository repository,
        CancellationToken cancellationToken)
    {
        if (request.ProgramId is null && request.TimeSpecifiedOption is null)
        {
            // The compatibility surface preserves the upstream runtime error mapping.
            throw new InvalidOperationException("AddReservationOptionError");
        }

        var command = new CreateReserveCommand(
            request.AllowEndLack,
            request.ProgramId,
            request.ProgramId is not null || request.TimeSpecifiedOption is null
                ? null
                : new TimeSpecifiedReserve(
                    request.TimeSpecifiedOption.Name,
                    request.TimeSpecifiedOption.ChannelId,
                    request.TimeSpecifiedOption.StartAt,
                    request.TimeSpecifiedOption.EndAt),
            request.Tags,
            request.SaveOption is null
                ? null
                : new ReserveSaveSettings(
                    request.SaveOption.ParentDirectoryName,
                    request.SaveOption.Directory,
                    request.SaveOption.RecordedFormat),
            request.EncodeOption is null
                ? null
                : new ReserveEncodeSettings(
                    request.EncodeOption.Mode1,
                    request.EncodeOption.EncodeParentDirectoryName1,
                    request.EncodeOption.Directory1,
                    request.EncodeOption.Mode2,
                    request.EncodeOption.EncodeParentDirectoryName2,
                    request.EncodeOption.Directory2,
                    request.EncodeOption.Mode3,
                    request.EncodeOption.EncodeParentDirectoryName3,
                    request.EncodeOption.Directory3,
                    request.EncodeOption.IsDeleteOriginalAfterEncode),
            request.Priority);
        long id = await repository.AddAsync(command, cancellationToken);

        return Results.Json(
            new AddedReserveResponse(id),
            statusCode: StatusCodes.Status201Created);
    }

    private static async Task<IResult> GetVersionAsync(
        IVersionRepository repository,
        CancellationToken cancellationToken)
    {
        string version = await repository.GetAsync(cancellationToken);
        return Results.Ok(new VersionResponse(version));
    }
}
