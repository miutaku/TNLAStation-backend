using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TNLAStation.Application.Abstractions;
using TNLAStation.Application.Models;
using TNLAStation.Domain;
using TNLAStation.Infrastructure.Configuration;
using TNLAStation.Infrastructure.Persistence;

namespace TNLAStation.Infrastructure.Repositories;

public sealed class PostgresEpgRepository(
    IDbContextFactory<EpgDbContext> contextFactory,
    IOptions<EpgOptions> options,
    TimeProvider timeProvider) : IEpgRepository, IEpgStore
{
    private readonly EpgOptions epgOptions = options.Value;

    public async ValueTask<IReadOnlyList<EpgChannel>> ListChannelsAsync(CancellationToken cancellationToken)
    {
        await using EpgDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);
        EpgChannel[] channels = await context.Channels
            .AsNoTracking()
            .Select(entity => entity.ToDomain())
            .ToArrayAsync(cancellationToken);
        return EpgChannelOrdering.Apply(channels, epgOptions);
    }

    public async ValueTask<EpgChannel?> GetChannelAsync(long channelId, CancellationToken cancellationToken)
    {
        await using EpgDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);
        EpgChannelEntity? entity = await context.Channels.AsNoTracking()
            .SingleOrDefaultAsync(channel => channel.Id == channelId, cancellationToken);
        return entity?.ToDomain();
    }

    public async ValueTask<EpgProgram?> GetProgramAsync(long programId, CancellationToken cancellationToken)
    {
        await using EpgDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);
        EpgProgramEntity? entity = await context.Programs.AsNoTracking()
            .SingleOrDefaultAsync(program => program.Id == programId, cancellationToken);
        return entity?.ToDomain();
    }

    public async ValueTask<IReadOnlyList<EpgProgram>> FindProgramsByIdsAsync(
        IReadOnlyList<long> programIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(programIds);
        if (programIds.Count == 0)
        {
            return [];
        }

        long[] ids = [.. programIds];
        await using EpgDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);
        EpgProgramEntity[] entities = await context.Programs.AsNoTracking()
            .Where(program => ids.Contains(program.Id))
            .ToArrayAsync(cancellationToken);
        return [.. entities.Select(entity => entity.ToDomain())];
    }

    public async ValueTask<IReadOnlyList<EpgProgram>> FindProgramsAsync(
        EpgScheduleQuery query,
        CancellationToken cancellationToken)
    {
        await using EpgDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);
        IQueryable<EpgProgramEntity> programs = context.Programs.AsNoTracking()
            .Where(program => program.StartAt <= query.EndAt && program.EndAt >= query.StartAt);

        if (query.ChannelId is not null)
        {
            programs = programs.Where(program => program.ChannelId == query.ChannelId.Value);
        }
        else if (query.ChannelTypes is { Count: > 0 })
        {
            programs = programs.Where(program => query.ChannelTypes.Contains(program.ChannelType));
        }

        if (query.IsFree is not null)
        {
            programs = programs.Where(program => program.IsFree == query.IsFree.Value);
        }

        EpgProgramEntity[] entities = await programs.OrderBy(program => program.StartAt)
            .ToArrayAsync(cancellationToken);
        return entities.Select(entity => entity.ToDomain()).ToArray();
    }

    public async ValueTask<IReadOnlyList<EpgProgram>> SearchProgramsAsync(
        EpgSearchQuery query,
        CancellationToken cancellationToken)
    {
        EpgSearchPolicy.Validate(query);

        DateTimeOffset now = timeProvider.GetUtcNow();
        await using EpgDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);
        EpgProgramEntity[] candidates = await context.Programs.AsNoTracking()
            .Where(program => program.EndAt >= now)
            .OrderBy(program => program.StartAt)
            .ToArrayAsync(cancellationToken);
        IEnumerable<EpgProgram> result = candidates
            .Select(entity => entity.ToDomain())
            .Where(program => EpgSearchPolicy.Matches(program, query, now));
        if (query.Limit is not null)
        {
            result = result.Take(query.Limit.Value);
        }

        return result.ToArray();
    }

    public async ValueTask ReplaceSnapshotAsync(EpgSnapshot snapshot, CancellationToken cancellationToken)
    {
        await using EpgDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        Dictionary<long, EpgChannelEntity> existingChannels = await context.Channels
            .ToDictionaryAsync(channel => channel.Id, cancellationToken);
        HashSet<long> incomingChannelIds = snapshot.Channels.Select(channel => channel.Id).ToHashSet();
        foreach (EpgChannel channel in snapshot.Channels)
        {
            if (existingChannels.TryGetValue(channel.Id, out EpgChannelEntity? entity))
            {
                EpgEntityMapper.UpdateEntity(entity, channel, snapshot.CapturedAt);
            }
            else
            {
                context.Channels.Add(EpgEntityMapper.CreateEntity(channel, snapshot.CapturedAt));
            }
        }

        await context.SaveChangesAsync(cancellationToken);

        Dictionary<long, EpgProgramEntity> existingPrograms = await context.Programs
            .ToDictionaryAsync(program => program.Id, cancellationToken);
        HashSet<long> incomingProgramIds = snapshot.Programs.Select(program => program.Id).ToHashSet();
        foreach (EpgProgram program in snapshot.Programs)
        {
            if (existingPrograms.TryGetValue(program.Id, out EpgProgramEntity? entity))
            {
                EpgEntityMapper.UpdateEntity(entity, program);
            }
            else
            {
                context.Programs.Add(EpgEntityMapper.CreateEntity(program));
            }
        }

        context.Programs.RemoveRange(existingPrograms.Values.Where(program => !incomingProgramIds.Contains(program.Id)));
        await context.SaveChangesAsync(cancellationToken);

        context.Channels.RemoveRange(existingChannels.Values.Where(channel => !incomingChannelIds.Contains(channel.Id)));
        EpgSyncStateEntity state = await GetOrCreateSyncStateAsync(context, cancellationToken);
        state.Generation++;
        state.NeedsFullSync = false;
        state.LastAttemptAt = snapshot.CapturedAt;
        state.LastSuccessAt = snapshot.CapturedAt;
        state.LastError = null;
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async ValueTask ApplyChangesAsync(
        IReadOnlyList<EpgChannel> changedChannels,
        IReadOnlyList<EpgProgram> upsertPrograms,
        IReadOnlyList<long> deleteProgramIds,
        DateTimeOffset streamEventAt,
        CancellationToken cancellationToken)
    {
        await using EpgDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        long[] channelIds = changedChannels.Select(channel => channel.Id).Distinct().ToArray();
        Dictionary<long, EpgChannelEntity> existingChannels = await context.Channels
            .Where(channel => channelIds.Contains(channel.Id))
            .ToDictionaryAsync(channel => channel.Id, cancellationToken);
        foreach (EpgChannel channel in changedChannels)
        {
            if (existingChannels.TryGetValue(channel.Id, out EpgChannelEntity? entity))
            {
                EpgEntityMapper.UpdateEntity(entity, channel, streamEventAt);
            }
            else
            {
                context.Channels.Add(EpgEntityMapper.CreateEntity(channel, streamEventAt));
            }
        }

        await context.SaveChangesAsync(cancellationToken);

        long[] upsertIds = upsertPrograms.Select(program => program.Id).Distinct().ToArray();
        Dictionary<long, EpgProgramEntity> existingPrograms = await context.Programs
            .Where(program => upsertIds.Contains(program.Id))
            .ToDictionaryAsync(program => program.Id, cancellationToken);
        foreach (EpgProgram program in upsertPrograms)
        {
            if (existingPrograms.TryGetValue(program.Id, out EpgProgramEntity? entity))
            {
                EpgEntityMapper.UpdateEntity(entity, program);
            }
            else
            {
                context.Programs.Add(EpgEntityMapper.CreateEntity(program));
            }
        }

        HashSet<long> upsertIdSet = upsertIds.ToHashSet();
        long[] effectiveDeletes = deleteProgramIds.Where(id => !upsertIdSet.Contains(id)).Distinct().ToArray();
        if (effectiveDeletes.Length > 0)
        {
            await context.Programs.Where(program => effectiveDeletes.Contains(program.Id))
                .ExecuteDeleteAsync(cancellationToken);
        }

        EpgSyncStateEntity state = await GetOrCreateSyncStateAsync(context, cancellationToken);
        state.LastStreamEventAt = streamEventAt;
        state.LastError = null;
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async ValueTask DeleteProgramsEndingBeforeAsync(
        DateTimeOffset threshold,
        CancellationToken cancellationToken)
    {
        await using EpgDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await context.Programs.Where(program => program.EndAt < threshold).ExecuteDeleteAsync(cancellationToken);
    }

    public async ValueTask RecordSyncFailureAsync(
        DateTimeOffset attemptedAt,
        string failureMessage,
        CancellationToken cancellationToken)
    {
        await using EpgDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);
        EpgSyncStateEntity state = await GetOrCreateSyncStateAsync(context, cancellationToken);
        state.NeedsFullSync = true;
        state.LastAttemptAt = attemptedAt;
        state.LastError = failureMessage;
        await context.SaveChangesAsync(cancellationToken);
    }

    private static async ValueTask<EpgSyncStateEntity> GetOrCreateSyncStateAsync(
        EpgDbContext context,
        CancellationToken cancellationToken)
    {
        EpgSyncStateEntity? state = await context.SyncStates.SingleOrDefaultAsync(
            item => item.SingletonId == 1,
            cancellationToken);
        if (state is not null)
        {
            return state;
        }

        state = new EpgSyncStateEntity { SingletonId = 1 };
        context.SyncStates.Add(state);
        return state;
    }
}
