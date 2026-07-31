using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using TNLAStation.Api.Contracts;
using TNLAStation.Application.Abstractions;
using TNLAStation.Application.Models;
using TNLAStation.Domain;
using TNLAStation.Infrastructure.Configuration;

namespace TNLAStation.Api.Endpoints;

/// <summary>
/// 中身が空になりうる一覧。録画していない、エンコード待ちがない、tag を作っていない、といった
/// 状態は障害ではないので、空の配列を返す。実装が無いからと 404 を返すと、画面はそれを
/// 取得失敗として扱ってしまう。
/// </summary>
internal static class CollectionEndpoints
{
    public static IEndpointRouteBuilder MapCollectionEndpoints(this IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder api = endpoints.MapGroup("/api");

        api.MapGet("/recording", GetRecordingAsync)
            .WithName("GetRecording")
            .WithSummary("録画中情報取得")
            .WithTags("recording")
            .Produces<RecordsResponse>();

        api.MapPost("/recording/resettimer", ResetRecordingTimerAsync)
            .WithName("ResetRecordingTimer")
            .WithSummary("録画予定を組み直す")
            .WithTags("recording")
            .Produces<ResultCodeResponse>();

        api.MapGet("/recorded/options", GetRecordedOptionsAsync)
            .WithName("GetRecordedOptions")
            .WithSummary("録画検索オプションを取得")
            .WithTags("recorded")
            .Produces<RecordedSearchOptionsResponse>();

        api.MapGet("/reserves/cnts", GetReserveCountsAsync)
            .WithName("GetReserveCounts")
            .WithSummary("予約数取得")
            .WithTags("reserves")
            .Produces<ReserveCountsResponse>();

        api.MapGet("/encode", GetEncodeAsync)
            .WithName("GetEncode")
            .WithSummary("エンコード情報取得")
            .WithTags("encode")
            .Produces<EncodeInfoResponse>();

        api.MapPost("/encode", AddEncodeAsync)
            .WithName("AddEncode")
            .WithSummary("エンコード追加")
            .WithTags("encode")
            .Produces<AddedEncodeResponse>(StatusCodes.Status201Created);

        api.MapDelete("/encode/{encodeId}", CancelEncodeAsync)
            .WithName("CancelEncode")
            .WithSummary("エンコード取り消し")
            .WithTags("encode")
            .Produces<ResultCodeResponse>();

        api.MapDelete("/recorded/{recordedId}/encode", CancelRecordedEncodeAsync)
            .WithName("CancelRecordedEncode")
            .WithSummary("録画に紐づくエンコードを取り消す")
            .WithTags("encode")
            .Produces<ResultCodeResponse>();

        api.MapGet("/streams", GetStreamsAsync)
            .WithName("GetStreams")
            .WithSummary("ストリーム情報取得")
            .WithTags("streams")
            .Produces<StreamInfoResponse>();

        api.MapGet("/tags", GetTagsAsync)
            .WithName("GetTags")
            .WithSummary("録画タグ情報取得")
            .WithTags("tags")
            .Produces<RecordedTagsResponse>();

        return endpoints;
    }

    private static async Task<IResult> GetRecordingAsync(
        IRecordingRepository repository,
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
            return Results.Json(
                new OpenApiValidationErrorResponse(
                    StatusCodes.Status400BadRequest,
                    [new OpenApiValidationError("must be >= 0")]),
                statusCode: StatusCodes.Status400BadRequest);
        }

        Page<RecordedProgram> page = await repository.ListAsync(
            new RecordedQuery(isHalfWidth, offset, limit, isReverse, ruleId, channelId, genre, keyword, hasOriginalFile),
            cancellationToken);

        return Results.Ok(new RecordsResponse(
            page.Items.Select(item => item.ToResponse(isHalfWidth)).ToArray(),
            page.Total));
    }

    /// <summary>
    /// 録画の予定を組み直す。番組表が動いたのに反映が遅れているときの手動の一押し。
    /// </summary>
    private static async Task<IResult> ResetRecordingTimerAsync(
        IReserveGenerationTrigger trigger,
        CancellationToken cancellationToken)
    {
        await trigger.RequestAsync(cancellationToken);
        return Results.Ok(new ResultCodeResponse(StatusCodes.Status200OK));
    }

    private static async Task<IResult> GetRecordedOptionsAsync(
        IRecordedRepository repository,
        IRecordingRepository recording,
        CancellationToken cancellationToken)
    {
        // 検索の選択肢は録画済みの中身から作る。録画が 1 件も無ければ選択肢も空になる。
        // EPGStation は録画中かどうかを区別せず数えるので、録画中も合わせて集計する。
        RecordedQuery query = new(IsHalfWidth: false, Offset: 0, Limit: int.MaxValue);
        Page<RecordedProgram> page = await repository.ListAsync(query, cancellationToken);
        Page<RecordedProgram> recordingPage = await recording.ListAsync(query, cancellationToken);
        IReadOnlyList<RecordedProgram> items = [.. page.Items, .. recordingPage.Items];

        RecordedChannelListItemResponse[] channels = items
            .GroupBy(item => item.ChannelId)
            .OrderBy(group => group.Key)
            .Select(group => new RecordedChannelListItemResponse(group.Count(), group.Key))
            .ToArray();
        RecordedGenreListItemResponse[] genres = items
            .Where(item => item.Genre1 is not null)
            .GroupBy(item => item.Genre1!.Value)
            .OrderBy(group => group.Key)
            .Select(group => new RecordedGenreListItemResponse(group.Count(), group.Key))
            .ToArray();

        return Results.Ok(new RecordedSearchOptionsResponse(channels, genres));
    }

    private static async Task<IResult> GetReserveCountsAsync(
        IReserveRepository repository,
        CancellationToken cancellationToken)
    {
        Page<Reservation> page = await repository.ListAsync(
            new ReserveQuery(IsHalfWidth: false, Offset: 0, Limit: int.MaxValue, Type: "all"),
            cancellationToken);

        // 1 件の予約は 1 つの種別にだけ数える。EPGStation は skip と overlap を優先し、
        // どちらでもない競合を conflicts、残りを normal として数える。
        int skips = page.Items.Count(reserve => reserve.IsSkip);
        int overlaps = page.Items.Count(reserve => !reserve.IsSkip && reserve.IsOverlap);
        int conflicts = page.Items.Count(reserve => !reserve.IsSkip && !reserve.IsOverlap && reserve.IsConflict);
        int normal = page.Items.Count - skips - overlaps - conflicts;

        return Results.Ok(new ReserveCountsResponse(normal, conflicts, skips, overlaps));
    }

    private static async Task<IResult> GetEncodeAsync(
        IEncodeQueueRepository repository,
        [FromQuery] bool isHalfWidth,
        CancellationToken cancellationToken = default)
    {
        EncodeTasks queue = await repository.GetAsync(cancellationToken);

        return Results.Ok(new EncodeInfoResponse(
            queue.Running.Select(item => item.ToResponse(isHalfWidth)).ToArray(),
            queue.Waiting.Select(item => item.ToResponse(isHalfWidth)).ToArray()));
    }

    private static async Task<IResult> AddEncodeAsync(
        AddEncodeRequest request,
        IEncodeTaskList queue,
        IOptions<EncodeOptions> encodeOptions,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.ParentDir is null && request.IsSaveSameDirectory != true)
        {
            throw new InvalidOperationException("OptionError");
        }

        // EPGStation (EncodeManageModel.push) は concurrentEncodeNum が 0 以下だと、待ち行列へ積む前に
        // 例外を投げる。綴りも EPGStation のまま (Cncurrent…) にしておかないと errors 文字列が変わる。
        if (encodeOptions.Value.ConcurrentEncodeNum <= 0)
        {
            throw new InvalidOperationException("CncurrentEncodeNumIsZero");
        }

        long encodeId = await queue.EnqueueAsync(
            new EncodeRequest(
                request.RecordedId,
                request.SourceVideoFileId,
                request.Mode,
                request.RemoveOriginal,
                request.ParentDir,
                request.Directory,
                request.IsSaveSameDirectory ?? false),
            cancellationToken);

        // EPGStation は responseJSON で 201 を返すだけで Location を付けない。付けると header が増える。
        return Results.Json(new AddedEncodeResponse(encodeId), statusCode: StatusCodes.Status201Created);
    }

    private static async Task<IResult> CancelEncodeAsync(
        long encodeId,
        IEncodeTaskList queue,
        CancellationToken cancellationToken)
    {
        // 既に終わったものを取り消すのは失敗ではない。押した時点で走り終えることがある。
        await queue.CancelAsync(encodeId, cancellationToken);
        return Results.Ok(new ResultCodeResponse(StatusCodes.Status200OK));
    }

    private static async Task<IResult> CancelRecordedEncodeAsync(
        long recordedId,
        IEncodeTaskList queue,
        CancellationToken cancellationToken)
    {
        await queue.CancelForRecordedAsync(recordedId, cancellationToken);
        return Results.Ok(new ResultCodeResponse(StatusCodes.Status200OK));
    }

    private static async Task<IResult> GetStreamsAsync(
        IStreamRepository repository,
        [FromQuery] bool isHalfWidth,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<StreamSession> sessions = await repository.ListAsync(cancellationToken);

        return Results.Ok(new StreamInfoResponse(sessions.Select(session => new StreamInfoItemResponse(
            session.StreamId,
            session.Type,
            session.Mode,
            session.IsEnable,
            session.ChannelId,
            session.Name)
        {
            ProgramId = session.ProgramId,
            VideoFileId = session.VideoFileId,
            StartAt = session.StartAt,
            EndAt = session.EndAt,
            Description = session.Description,
            Extended = session.Extended,
        }).ToArray()));
    }

    private static async Task<IResult> GetTagsAsync(
        IRecordedTagRepository repository,
        [FromQuery] int? offset = null,
        [FromQuery] int? limit = null,
        [FromQuery] string? name = null,
        [FromQuery(Name = "excludeTagId")] long[]? excludeTagIds = null,
        CancellationToken cancellationToken = default)
    {
        Page<RecordedTag> page = await repository.ListAsync(
            new RecordedTagQuery(offset, limit, name, excludeTagIds),
            cancellationToken);

        return Results.Ok(new RecordedTagsResponse(
            page.Items.Select(tag => new RecordedTagResponse(tag.Id, tag.Name, tag.Color)).ToArray(),
            page.Total));
    }

    private static EncodeProgramItemResponse ToResponse(this EncodeQueueItem item, bool isHalfWidth) =>
        new(item.Id, item.Mode, item.Recorded.ToResponse(isHalfWidth))
        {
            Percent = item.Percent,
            Log = item.Log,
        };
}
