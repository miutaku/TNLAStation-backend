using Microsoft.Extensions.Options;
using TNLAStation.Application.Abstractions;
using TNLAStation.Application.Models;
using TNLAStation.Domain;
using TNLAStation.Infrastructure.Configuration;

namespace TNLAStation.Infrastructure.Repositories;

public sealed class InMemoryEpgRepository : IEpgRepository, IEpgStore
{
    private readonly object gate = new();
    private readonly EpgOptions options;
    private readonly TimeProvider timeProvider;
    private Dictionary<long, EpgChannel> channels;
    private Dictionary<long, EpgProgram> programs;
    private EpgSyncState syncState = new(0, true, null, null, null, null);

    public InMemoryEpgRepository(IOptions<EpgOptions> options, TimeProvider timeProvider)
    {
        this.options = options.Value;
        this.timeProvider = timeProvider;

        var channel = new EpgChannel(
            Id: 3_273_601_024,
            ServiceId: 1024,
            NetworkId: 32736,
            Name: "ＮＨＫ総合１・東京",
            HalfWidthName: "NHK総合1・東京",
            RemoteControlKeyId: 1,
            HasLogoData: true,
            ChannelTypeId: 0,
            ChannelType: "GR",
            Channel: "27",
            ServiceType: 1);
        DateTimeOffset now = timeProvider.GetUtcNow();
        DateTimeOffset startAt = now.AddMinutes(-15);
        var program = new EpgProgram(
            Id: 327_360_102_400_123,
            UpdateTime: now,
            ChannelId: channel.Id,
            EventId: 123,
            ServiceId: channel.ServiceId,
            NetworkId: channel.NetworkId,
            StartAt: startAt,
            EndAt: startAt.AddHours(1),
            StartHour: TimeZoneInfo.ConvertTime(startAt, JapanTimeZone).Hour,
            Week: (int)TimeZoneInfo.ConvertTime(startAt, JapanTimeZone).DayOfWeek,
            DurationMilliseconds: 3_600_000,
            IsFree: true,
            Name: "モック放送中番組",
            HalfWidthName: "モック放送中番組",
            ShortName: "モック放送中番組",
            ChannelType: channel.ChannelType,
            Channel: channel.Channel,
            Description: "Phase 2 EPG のインメモリデータです。",
            HalfWidthDescription: "Phase 2 EPG のインメモリデータです。",
            RawExtended: new Dictionary<string, string> { ["補足"] = "固定データ" },
            RawHalfWidthExtended: new Dictionary<string, string> { ["補足"] = "固定データ" },
            Genre1: 0,
            SubGenre1: 0,
            VideoType: "mpeg2",
            VideoResolution: "1080i",
            AudioSamplingRate: 48000,
            AudioComponentType: 3);

        channels = new Dictionary<long, EpgChannel> { [channel.Id] = channel };
        programs = new Dictionary<long, EpgProgram> { [program.Id] = program };
    }

    private static TimeZoneInfo JapanTimeZone { get; } = CreateJapanTimeZone();

    public ValueTask<IReadOnlyList<EpgChannel>> ListChannelsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            return ValueTask.FromResult(EpgChannelOrdering.Apply(channels.Values, options));
        }
    }

    public ValueTask<EpgChannel?> GetChannelAsync(long channelId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            channels.TryGetValue(channelId, out EpgChannel? channel);
            return ValueTask.FromResult(channel);
        }
    }

    public ValueTask<EpgProgram?> GetProgramAsync(long programId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            programs.TryGetValue(programId, out EpgProgram? program);
            return ValueTask.FromResult(program);
        }
    }

    public ValueTask<IReadOnlyList<EpgProgram>> FindProgramsAsync(
        EpgScheduleQuery query,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            IEnumerable<EpgProgram> result = programs.Values.Where(program =>
                program.StartAt <= query.EndAt && program.EndAt >= query.StartAt);
            if (query.ChannelId is not null)
            {
                result = result.Where(program => program.ChannelId == query.ChannelId.Value);
            }
            else if (query.ChannelTypes is { Count: > 0 })
            {
                result = result.Where(program => query.ChannelTypes.Contains(program.ChannelType));
            }

            if (query.IsFree is not null)
            {
                result = result.Where(program => program.IsFree == query.IsFree.Value);
            }

            return ValueTask.FromResult<IReadOnlyList<EpgProgram>>(result.OrderBy(program => program.StartAt).ToArray());
        }
    }

    public ValueTask<IReadOnlyList<EpgProgram>> SearchProgramsAsync(
        EpgSearchQuery query,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EpgSearchPolicy.Validate(query);

        lock (gate)
        {
            IEnumerable<EpgProgram> result = programs.Values
                .Where(program => EpgSearchPolicy.Matches(program, query, timeProvider.GetUtcNow()))
                .OrderBy(program => program.StartAt);
            if (query.Limit is not null)
            {
                result = result.Take(query.Limit.Value);
            }

            return ValueTask.FromResult<IReadOnlyList<EpgProgram>>(result.ToArray());
        }
    }

    public ValueTask ReplaceSnapshotAsync(EpgSnapshot snapshot, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            channels = snapshot.Channels.ToDictionary(channel => channel.Id);
            programs = snapshot.Programs.ToDictionary(program => program.Id);
            syncState = syncState with
            {
                Generation = syncState.Generation + 1,
                NeedsFullSync = false,
                LastAttemptAt = snapshot.CapturedAt,
                LastSuccessAt = snapshot.CapturedAt,
                LastError = null
            };
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask ApplyChangesAsync(
        IReadOnlyList<EpgChannel> changedChannels,
        IReadOnlyList<EpgProgram> upsertPrograms,
        IReadOnlyList<long> deleteProgramIds,
        DateTimeOffset streamEventAt,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            foreach (EpgChannel channel in changedChannels)
            {
                channels[channel.Id] = channel;
            }

            foreach (long id in deleteProgramIds)
            {
                programs.Remove(id);
            }

            foreach (EpgProgram program in upsertPrograms)
            {
                programs[program.Id] = program;
            }

            syncState = syncState with { LastStreamEventAt = streamEventAt, LastError = null };
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask DeleteProgramsEndingBeforeAsync(
        DateTimeOffset threshold,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            foreach (long id in programs.Values.Where(program => program.EndAt < threshold).Select(program => program.Id).ToArray())
            {
                programs.Remove(id);
            }
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask RecordSyncFailureAsync(
        DateTimeOffset attemptedAt,
        string failureMessage,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            syncState = syncState with
            {
                NeedsFullSync = true,
                LastAttemptAt = attemptedAt,
                LastError = failureMessage
            };
        }

        return ValueTask.CompletedTask;
    }

    private static TimeZoneInfo CreateJapanTimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Asia/Tokyo");
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.CreateCustomTimeZone("Asia/Tokyo", TimeSpan.FromHours(9), "JST", "JST");
        }
    }
}
