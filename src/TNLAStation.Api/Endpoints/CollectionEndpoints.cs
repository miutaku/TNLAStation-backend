using Microsoft.AspNetCore.Mvc;
using TNLAStation.Api.Contracts;
using TNLAStation.Application.Abstractions;
using TNLAStation.Application.Models;
using TNLAStation.Domain;

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
            return Results.BadRequest(new ValidationErrorResponse("offset and limit must be greater than or equal to 0"));
        }

        Page<RecordedProgram> page = await repository.ListAsync(
            new RecordedQuery(isHalfWidth, offset, limit, isReverse, ruleId, channelId, genre, keyword, hasOriginalFile),
            cancellationToken);

        return Results.Ok(new RecordsResponse(
            page.Items.Select(item => item.ToResponse(isHalfWidth)).ToArray(),
            page.Total));
    }

    private static async Task<IResult> GetRecordedOptionsAsync(
        IRecordedRepository repository,
        CancellationToken cancellationToken)
    {
        // 検索の選択肢は録画済みの中身から作る。録画が 1 件も無ければ選択肢も空になる。
        Page<RecordedProgram> page = await repository.ListAsync(
            new RecordedQuery(IsHalfWidth: false, Offset: 0, Limit: int.MaxValue),
            cancellationToken);

        RecordedChannelListItemResponse[] channels = page.Items
            .GroupBy(item => item.ChannelId)
            .OrderBy(group => group.Key)
            .Select(group => new RecordedChannelListItemResponse(group.Count(), group.Key))
            .ToArray();
        RecordedGenreListItemResponse[] genres = page.Items
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

        // 1 件の予約は 1 つの種別にだけ数える。上流は skip と overlap を優先し、
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
        CancellationToken cancellationToken = default)
    {
        Page<RecordedTag> page = await repository.ListAsync(
            new RecordedTagQuery(offset, limit, name),
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
