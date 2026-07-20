using Microsoft.AspNetCore.Mvc;
using TNLAStation.Api.Contracts;
using TNLAStation.Application.Abstractions;
using TNLAStation.Application.Models;
using TNLAStation.Domain;

namespace TNLAStation.Api.Endpoints;

internal static class EpgPhaseTwoEndpoints
{
    public static IEndpointRouteBuilder MapEpgPhaseTwoEndpoints(this IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder api = endpoints.MapGroup("/api");

        api.MapGet("/channels", GetChannelsAsync)
            .WithName("GetChannels")
            .WithSummary("放送局情報取得")
            .WithTags("channels")
            .Produces<IReadOnlyList<ChannelItemResponse>>();

        api.MapGet("/channels/{channelId:long}/logo", GetChannelLogoAsync)
            .WithName("GetChannelLogo")
            .WithSummary("放送局ロゴ取得")
            .WithTags("channels")
            .Produces(StatusCodes.Status200OK, contentType: "image/png")
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound)
            .Produces<ErrorResponse>(StatusCodes.Status500InternalServerError);

        api.MapGet("/schedules", GetSchedulesAsync)
            .WithName("GetSchedules")
            .WithSummary("番組表情報取得")
            .WithTags("schedules")
            .Produces<IReadOnlyList<ScheduleResponse>>();

        api.MapGet("/schedules/broadcasting", GetBroadcastingSchedulesAsync)
            .WithName("GetBroadcastingSchedules")
            .WithSummary("放映中の番組情報取得")
            .WithTags("schedules")
            .Produces<IReadOnlyList<ScheduleResponse>>();

        api.MapGet("/schedules/detail/{programId:long}", GetScheduleDetailAsync)
            .WithName("GetScheduleDetail")
            .WithSummary("指定された番組表情報取得")
            .WithTags("schedules")
            .Produces<ScheduleProgramResponse>()
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound);

        api.MapPost("/schedules/search", SearchSchedulesAsync)
            .WithName("SearchSchedules")
            .WithSummary("番組検索結果を取得")
            .WithTags("schedules")
            .Accepts<ScheduleSearchRequest>("application/json")
            .Produces<IReadOnlyList<ScheduleProgramResponse>>();

        api.MapGet("/schedules/{channelId:long}", GetChannelSchedulesAsync)
            .WithName("GetChannelSchedules")
            .WithSummary("指定された放送局の番組表情報取得")
            .WithTags("schedules")
            .Produces<IReadOnlyList<ScheduleResponse>>();

        api.MapGet("/storages", GetStoragesAsync)
            .WithName("GetStorages")
            .WithSummary("ストレージ情報取得")
            .WithTags("storages")
            .Produces<StorageInfoResponse>()
            .Produces<ErrorResponse>(StatusCodes.Status500InternalServerError);

        return endpoints;
    }

    private static async Task<IResult> GetChannelsAsync(
        IEpgRepository repository,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<EpgChannel> channels = await repository.ListChannelsAsync(cancellationToken);
        return Results.Ok(channels.Select(channel => channel.ToChannelResponse()).ToArray());
    }

    private static async Task<IResult> GetChannelLogoAsync(
        long channelId,
        IEpgRepository repository,
        IChannelLogoProvider logoProvider,
        CancellationToken cancellationToken)
    {
        EpgChannel? channel = await repository.GetChannelAsync(channelId, cancellationToken);
        if (channel is null || !channel.HasLogoData)
        {
            return Results.Json(
                new ErrorResponse(StatusCodes.Status404NotFound, "log file is not found"),
                statusCode: StatusCodes.Status404NotFound);
        }

        ReadOnlyMemory<byte> logo = await logoProvider.GetLogoAsync(channelId, cancellationToken);
        return Results.File(logo.ToArray(), "image/png");
    }

    private static async Task<IResult> GetSchedulesAsync(
        IEpgRepository repository,
        [FromQuery] long startAt,
        [FromQuery] long endAt,
        [FromQuery] bool isHalfWidth,
        [FromQuery(Name = "GR")] bool gr,
        [FromQuery(Name = "BS")] bool bs,
        [FromQuery(Name = "CS")] bool cs,
        [FromQuery(Name = "SKY")] bool sky,
        [FromQuery] bool? needsRawExtended = null,
        [FromQuery] bool? isFree = null,
        CancellationToken cancellationToken = default)
    {
        string[] channelTypes = CreateChannelTypes(gr, bs, cs, sky);
        if (channelTypes.Length == 0)
        {
            throw new InvalidOperationException("GetScheduleTypesError");
        }

        IReadOnlyList<EpgChannel> channels = await repository.ListChannelsAsync(cancellationToken);
        IReadOnlyList<EpgProgram> programs = await repository.FindProgramsAsync(
            new EpgScheduleQuery(
                DateTimeOffset.FromUnixTimeMilliseconds(startAt),
                DateTimeOffset.FromUnixTimeMilliseconds(endAt),
                channelTypes,
                IsFree: isFree),
            cancellationToken);
        return Results.Ok(CreateSchedules(
            channels.Where(channel => channelTypes.Contains(channel.ChannelType, StringComparer.Ordinal)),
            programs,
            isHalfWidth,
            needsRawExtended == true,
            includeChannelsWithoutPrograms: false));
    }

    private static async Task<IResult> GetChannelSchedulesAsync(
        long channelId,
        IEpgRepository repository,
        [FromQuery] long startAt,
        [FromQuery] int days,
        [FromQuery] bool isHalfWidth,
        [FromQuery] bool? needsRawExtended = null,
        [FromQuery] bool? isFree = null,
        CancellationToken cancellationToken = default)
    {
        EpgChannel? channel = await repository.GetChannelAsync(channelId, cancellationToken);
        if (channel is null)
        {
            throw new InvalidOperationException("ChannelIsNotFound");
        }

        var result = new List<ScheduleResponse>();
        DateTimeOffset baseTime = DateTimeOffset.FromUnixTimeMilliseconds(startAt);
        for (int day = 0; day < days; day++)
        {
            DateTimeOffset endTime = baseTime.AddDays(1);
            IReadOnlyList<EpgProgram> programs = await repository.FindProgramsAsync(
                new EpgScheduleQuery(baseTime, endTime, ChannelId: channelId, IsFree: isFree),
                cancellationToken);
            result.Add(new ScheduleResponse(
                channel.ToScheduleChannelResponse(isHalfWidth),
                programs.Select(program => program.ToScheduleProgramResponse(
                    isHalfWidth,
                    needsRawExtended == true)).ToArray()));
            baseTime = endTime;
        }

        return Results.Ok(result);
    }

    private static async Task<IResult> GetBroadcastingSchedulesAsync(
        IEpgRepository repository,
        TimeProvider timeProvider,
        [FromQuery] bool isHalfWidth,
        [FromQuery] long? time = null,
        CancellationToken cancellationToken = default)
    {
        DateTimeOffset target = timeProvider.GetUtcNow().AddMilliseconds(time ?? 0);
        IReadOnlyList<EpgChannel> channels = await repository.ListChannelsAsync(cancellationToken);
        IReadOnlyList<EpgProgram> programs = await repository.FindProgramsAsync(
            new EpgScheduleQuery(target, target, ["GR", "BS", "CS", "SKY"]),
            cancellationToken);
        ScheduleResponse[] schedules = CreateSchedules(
                channels,
                programs,
                isHalfWidth,
                needsRawExtended: true,
                includeChannelsWithoutPrograms: false)
            .Select(schedule => schedule.Programs.Count > 1
                ? schedule with { Programs = [schedule.Programs[0]] }
                : schedule)
            .ToArray();
        return Results.Ok(schedules);
    }

    private static async Task<IResult> GetScheduleDetailAsync(
        long programId,
        IEpgRepository repository,
        [FromQuery] bool isHalfWidth,
        CancellationToken cancellationToken = default)
    {
        EpgProgram? program = await repository.GetProgramAsync(programId, cancellationToken);
        return program is null
            ? Results.Json(
                new ErrorResponse(StatusCodes.Status404NotFound, "program is not found"),
                statusCode: StatusCodes.Status404NotFound)
            : Results.Ok(program.ToScheduleProgramResponse(isHalfWidth, needsRawExtended: true));
    }

    private static async Task<IResult> SearchSchedulesAsync(
        ScheduleSearchRequest request,
        IEpgRepository repository,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<EpgProgram> programs = await repository.SearchProgramsAsync(
            request.ToQuery(),
            cancellationToken);
        return Results.Ok(programs.Select(program => program.ToScheduleProgramResponse(
            request.IsHalfWidth,
            needsRawExtended: true)).ToArray());
    }

    private static async Task<IResult> GetStoragesAsync(
        IStorageRepository repository,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<StorageUsage> items = await repository.ListAsync(cancellationToken);
        return Results.Ok(items.ToResponse());
    }

    private static ScheduleResponse[] CreateSchedules(
        IEnumerable<EpgChannel> channels,
        IEnumerable<EpgProgram> programs,
        bool isHalfWidth,
        bool needsRawExtended,
        bool includeChannelsWithoutPrograms)
    {
        Dictionary<long, ScheduleProgramResponse[]> programsByChannel = programs
            .GroupBy(program => program.ChannelId)
            .ToDictionary(
                group => group.Key,
                group => group.Select(program => program.ToScheduleProgramResponse(
                    isHalfWidth,
                    needsRawExtended)).ToArray());
        var result = new List<ScheduleResponse>();
        foreach (EpgChannel channel in channels)
        {
            if (!programsByChannel.TryGetValue(channel.Id, out ScheduleProgramResponse[]? channelPrograms))
            {
                if (!includeChannelsWithoutPrograms)
                {
                    continue;
                }

                channelPrograms = [];
            }

            result.Add(new ScheduleResponse(
                channel.ToScheduleChannelResponse(isHalfWidth),
                channelPrograms));
        }

        return result.ToArray();
    }

    private static string[] CreateChannelTypes(bool gr, bool bs, bool cs, bool sky)
    {
        var result = new List<string>(4);
        if (gr)
        {
            result.Add("GR");
        }

        if (bs)
        {
            result.Add("BS");
        }

        if (cs)
        {
            result.Add("CS");
        }

        if (sky)
        {
            result.Add("SKY");
        }

        return result.ToArray();
    }
}
