using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TNLAStation.Application.Abstractions;
using TNLAStation.Application.Models;
using TNLAStation.Domain;
using TNLAStation.Infrastructure.Persistence;

namespace TNLAStation.Infrastructure.Repositories;

/// <summary>
/// 予約の永続化。予約そのものは番組の詳細を持たず、読むときに番組表と結合する。
/// 番組の内容は放送までに変わるので、予約を作った時点の写しを見せると古い説明が残る。
/// </summary>
public sealed class PostgresReserveRepository(
    IDbContextFactory<EpgDbContext> contextFactory,
    TimeProvider timeProvider) : IReserveRepository, IReserveStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async ValueTask<Page<Reservation>> ListAsync(ReserveQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        await using EpgDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);
        IQueryable<ReserveEntity> reserves = ApplyType(context.Reserves.AsNoTracking(), query.Type);
        if (query.RuleId is not null)
        {
            reserves = reserves.Where(reserve => reserve.RuleId == query.RuleId);
        }

        int total = await reserves.CountAsync(cancellationToken);
        ReserveEntity[] page = await reserves
            .OrderBy(reserve => reserve.StartAt)
            .ThenBy(reserve => reserve.Id)
            .Skip(query.Offset)
            .Take(query.Limit)
            .ToArrayAsync(cancellationToken);

        return new Page<Reservation>(await ToReservationsAsync(context, page, cancellationToken), total);
    }

    public async ValueTask<Reservation?> GetAsync(long reserveId, CancellationToken cancellationToken)
    {
        await using EpgDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);
        ReserveEntity? entity = await context.Reserves.AsNoTracking()
            .SingleOrDefaultAsync(reserve => reserve.Id == reserveId, cancellationToken);
        if (entity is null)
        {
            return null;
        }

        IReadOnlyList<Reservation> result = await ToReservationsAsync(context, [entity], cancellationToken);
        return result[0];
    }

    public async ValueTask<long> AddAsync(CreateReserveCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        await using EpgDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var entity = new ManualReserveEntity
        {
            ProgramId = command.ProgramId,
            IsTimeSpecified = command.TimeSpecified is not null,
            AllowEndLack = command.AllowEndLack,
            Priority = command.Priority,
            IsDeleteOriginalAfterEncode = command.Encode?.IsDeleteOriginalAfterEncode ?? false,
            TagsJson = command.Tags is { Count: > 0 } ? JsonSerializer.Serialize(command.Tags, JsonOptions) : null,
            ParentDirectoryName = command.Save?.ParentDirectoryName,
            Directory = command.Save?.Directory,
            RecordedFormat = command.Save?.RecordedFormat,
            Mode1 = command.Encode?.Mode1,
            ParentDirectoryName1 = command.Encode?.EncodeParentDirectoryName1,
            Directory1 = command.Encode?.Directory1,
            Mode2 = command.Encode?.Mode2,
            ParentDirectoryName2 = command.Encode?.EncodeParentDirectoryName2,
            Directory2 = command.Encode?.Directory2,
            Mode3 = command.Encode?.Mode3,
            ParentDirectoryName3 = command.Encode?.EncodeParentDirectoryName3,
            Directory3 = command.Encode?.Directory3,
            CreatedAt = timeProvider.GetUtcNow(),
        };

        if (command.TimeSpecified is { } specified)
        {
            EpgChannelEntity? channel = await context.Channels.AsNoTracking()
                .SingleOrDefaultAsync(item => item.Id == specified.ChannelId, cancellationToken);
            entity.ChannelId = specified.ChannelId;
            entity.ChannelType = channel?.ChannelType ?? throw new InvalidOperationException("ChannelIsNotFound");
            entity.StartAt = DateTimeOffset.FromUnixTimeMilliseconds(specified.StartAt);
            entity.EndAt = DateTimeOffset.FromUnixTimeMilliseconds(specified.EndAt);
            entity.Name = specified.Name;
            entity.HalfWidthName = specified.Name;
        }
        else
        {
            EpgProgramEntity program = await context.Programs.AsNoTracking()
                .SingleOrDefaultAsync(item => item.Id == command.ProgramId, cancellationToken)
                ?? throw new InvalidOperationException("ProgramIsNotFound");
            entity.ChannelId = program.ChannelId;
            entity.ChannelType = program.ChannelType;
            entity.StartAt = program.StartAt;
            entity.EndAt = program.EndAt;
            entity.Name = program.Name;
            entity.HalfWidthName = program.HalfWidthName;
        }

        context.ManualReserves.Add(entity);
        await context.SaveChangesAsync(cancellationToken);
        return entity.Id;
    }

    public async ValueTask<bool> DeleteAsync(long reserveId, CancellationToken cancellationToken)
    {
        await using EpgDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);
        ReserveEntity? reserve = await context.Reserves
            .SingleOrDefaultAsync(item => item.Id == reserveId, cancellationToken);
        if (reserve is null)
        {
            return false;
        }

        if (reserve.ManualReserveId is { } manualId)
        {
            await context.ManualReserves.Where(item => item.Id == manualId).ExecuteDeleteAsync(cancellationToken);
            await context.Reserves.Where(item => item.Id == reserveId).ExecuteDeleteAsync(cancellationToken);
            return true;
        }

        // ルールが作った予約は消しても次の生成で戻ってくる。消えたように見せると、また現れて
        // 驚くことになるので、録らないという指定として残す。
        return await SetSkipAsync(reserveId, isSkip: true, cancellationToken);
    }

    public async ValueTask<bool> SetSkipAsync(long reserveId, bool isSkip, CancellationToken cancellationToken)
    {
        await using EpgDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);
        ReserveEntity? reserve = await context.Reserves
            .SingleOrDefaultAsync(item => item.Id == reserveId, cancellationToken);
        if (reserve is null)
        {
            return false;
        }

        if (isSkip)
        {
            if (!await context.ReserveSkips.AnyAsync(skip => skip.Key == reserve.Key, cancellationToken))
            {
                context.ReserveSkips.Add(new ReserveSkipEntity
                {
                    Key = reserve.Key,
                    CreatedAt = timeProvider.GetUtcNow(),
                });
            }
        }
        else
        {
            await context.ReserveSkips
                .Where(skip => skip.Key == reserve.Key)
                .ExecuteDeleteAsync(cancellationToken);
        }

        // 次の生成を待たずに一覧へ反映する。押した直後に変わらないと、効いたか分からない。
        reserve.IsSkip = isSkip;
        await context.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async ValueTask<IReadOnlyList<ManualReserve>> ListManualReservesAsync(
        CancellationToken cancellationToken)
    {
        await using EpgDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);
        ManualReserveEntity[] entities = await context.ManualReserves.AsNoTracking()
            .OrderBy(item => item.Id)
            .ToArrayAsync(cancellationToken);

        return [.. entities.Select(item => new ManualReserve(
            item.Id,
            item.ChannelId,
            item.ChannelType,
            item.StartAt,
            item.EndAt,
            item.Name,
            item.ProgramId,
            item.IsTimeSpecified,
            Priority: item.Priority))];
    }

    public async ValueTask<IReadOnlyDictionary<string, bool>> ListSkipStatesAsync(
        CancellationToken cancellationToken)
    {
        await using EpgDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);
        string[] keys = await context.ReserveSkips.AsNoTracking()
            .Select(skip => skip.Key)
            .ToArrayAsync(cancellationToken);

        return keys.ToDictionary(key => key, _ => true, StringComparer.Ordinal);
    }

    public async ValueTask ReplaceAsync(
        IReadOnlyList<ReserveAssignment> assignments,
        DateTimeOffset generatedAt,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(assignments);

        await using EpgDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        await context.Reserves.ExecuteDeleteAsync(cancellationToken);
        context.Reserves.AddRange(assignments.Select(assignment => new ReserveEntity
        {
            Key = assignment.Target.Key,
            Source = assignment.Target.Source.ToString(),
            RuleId = assignment.Target.RuleId,
            ProgramId = assignment.Target.ProgramId,
            ManualReserveId = assignment.Target.ManualReserveId,
            ChannelId = assignment.Target.ChannelId,
            ChannelType = assignment.Target.ChannelType,
            StartAt = assignment.Target.StartAt,
            EndAt = assignment.Target.EndAt,
            Name = assignment.Target.Name,
            HalfWidthName = assignment.Target.Name,
            Priority = assignment.Target.Priority,
            IsSkip = assignment.Target.IsSkip,
            IsConflict = assignment.IsConflict,
            IsOverlap = assignment.Target.IsOverlap,
            TunerIndex = assignment.TunerIndex,
            GeneratedAt = generatedAt,
        }));

        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private static IQueryable<ReserveEntity> ApplyType(IQueryable<ReserveEntity> reserves, string? type) =>
        type switch
        {
            "normal" => reserves.Where(item => !item.IsConflict && !item.IsSkip && !item.IsOverlap),
            "conflict" => reserves.Where(item => item.IsConflict),
            "skip" => reserves.Where(item => item.IsSkip),
            "overlap" => reserves.Where(item => item.IsOverlap),
            _ => reserves,
        };

    private static async Task<IReadOnlyList<Reservation>> ToReservationsAsync(
        EpgDbContext context,
        IReadOnlyList<ReserveEntity> reserves,
        CancellationToken cancellationToken)
    {
        long[] programIds = [.. reserves.Where(item => item.ProgramId is not null)
            .Select(item => item.ProgramId!.Value)];
        long[] manualIds = [.. reserves.Where(item => item.ManualReserveId is not null)
            .Select(item => item.ManualReserveId!.Value)];

        Dictionary<long, EpgProgramEntity> programs = await context.Programs.AsNoTracking()
            .Where(program => programIds.Contains(program.Id))
            .ToDictionaryAsync(program => program.Id, cancellationToken);
        Dictionary<long, ManualReserveEntity> manuals = await context.ManualReserves.AsNoTracking()
            .Where(manual => manualIds.Contains(manual.Id))
            .ToDictionaryAsync(manual => manual.Id, cancellationToken);

        return [.. reserves.Select(reserve => reserve.ToDomain(
            reserve.ProgramId is { } programId && programs.TryGetValue(programId, out EpgProgramEntity? program)
                ? program
                : null,
            reserve.ManualReserveId is { } manualId && manuals.TryGetValue(manualId, out ManualReserveEntity? manual)
                ? manual
                : null))];
    }
}
