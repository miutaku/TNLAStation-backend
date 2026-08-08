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

        // EPGStation は録画が終わった予約を Recorded へ移して Reserve からは消す。この実装はまだ
        // 録画完了時にその移動をしないので、代わりに読むときに終了済みを外して同じ見た目にする。
        DateTimeOffset now = timeProvider.GetUtcNow();
        IQueryable<ReserveEntity> reserves = ApplyType(
            context.Reserves.AsNoTracking().Where(reserve => reserve.EndAt > now),
            query.Type);
        if (query.RuleId == 0)
        {
            reserves = reserves.Where(reserve => reserve.RuleId == null);
        }
        else if (query.RuleId is not null)
        {
            reserves = reserves.Where(reserve => reserve.RuleId == query.RuleId);
        }

        if (query.ChannelId is { } channelId)
        {
            reserves = reserves.Where(reserve => reserve.ChannelId == channelId);
        }

        if (query.Genre is { } genre)
        {
            reserves = reserves.Where(reserve =>
                reserve.ProgramId != null &&
                context.Programs.Any(program =>
                    program.Id == reserve.ProgramId &&
                    (program.Genre1 == genre || program.Genre2 == genre || program.Genre3 == genre)));
        }

        if (!string.IsNullOrWhiteSpace(query.Keyword))
        {
            string pattern = $"%{query.Keyword.Trim()}%";
            reserves = reserves.Where(reserve =>
                EF.Functions.ILike(reserve.Name, pattern) ||
                EF.Functions.ILike(reserve.HalfWidthName, pattern) ||
                (reserve.ProgramId != null &&
                    context.Programs.Any(program =>
                        program.Id == reserve.ProgramId &&
                        ((program.Description != null && EF.Functions.ILike(program.Description, pattern)) ||
                         (program.HalfWidthDescription != null &&
                            EF.Functions.ILike(program.HalfWidthDescription, pattern)) ||
                         (program.Extended != null && EF.Functions.ILike(program.Extended, pattern)) ||
                         (program.HalfWidthExtended != null &&
                            EF.Functions.ILike(program.HalfWidthExtended, pattern))))));
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

        // EPGStation は番組指定の手動予約を、ルール予約であっても既に何かが同じ番組を掴んでいれば
        // 拒否する (二重登録の入口を防ぐだけで、ルール側からの二重取得までは防いでいない非対称な仕様)。
        if (command.ProgramId is { } programId &&
            await context.Reserves.AnyAsync(item => item.ProgramId == programId, cancellationToken))
        {
            throw new InvalidOperationException("ReservationManageModelReservedError");
        }

        if (command.TimeSpecified is { } specifiedCheck)
        {
            if (specifiedCheck.EndAt <= timeProvider.GetUtcNow().ToUnixTimeMilliseconds())
            {
                throw new InvalidOperationException("TimeSpecifiedOptionError");
            }

            DateTimeOffset specifiedStart = DateTimeOffset.FromUnixTimeMilliseconds(specifiedCheck.StartAt);
            DateTimeOffset specifiedEnd = DateTimeOffset.FromUnixTimeMilliseconds(specifiedCheck.EndAt);
            bool duplicate = await context.Reserves.AnyAsync(item =>
                item.RuleId == null &&
                item.ChannelId == specifiedCheck.ChannelId &&
                item.StartAt == specifiedStart &&
                item.EndAt == specifiedEnd,
                cancellationToken);
            if (duplicate)
            {
                throw new InvalidOperationException("AddReservationConflictError");
            }
        }

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

        ReserveStateEntity state = await GetOrCreateStateAsync(context, reserve.Key, cancellationToken);
        state.IsSkip = isSkip;

        // 次の生成を待たずに一覧へ反映する。押した直後に変わらないと、効いたか分からない。
        reserve.IsSkip = isSkip;
        await context.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async ValueTask<bool> ClearOverlapAsync(long reserveId, CancellationToken cancellationToken)
    {
        await using EpgDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);
        ReserveEntity? reserve = await context.Reserves
            .SingleOrDefaultAsync(item => item.Id == reserveId, cancellationToken);
        if (reserve is null)
        {
            return false;
        }

        ReserveStateEntity state = await GetOrCreateStateAsync(context, reserve.Key, cancellationToken);
        state.IsOverlapCleared = true;
        reserve.IsOverlap = false;
        await context.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <summary>
    /// 手動予約を差し替える。時刻や番組そのものは変えない。変えたいなら別の予約であって、
    /// 同じ予約を書き換えるのとは意味が違う。
    /// </summary>
    public async ValueTask<bool> UpdateAsync(
        long reserveId,
        CreateReserveCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        await using EpgDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);
        ReserveEntity? reserve = await context.Reserves.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == reserveId, cancellationToken);
        if (reserve is null)
        {
            return false;
        }

        if (reserve.ManualReserveId is not { } manualId)
        {
            ReserveStateEntity state = await GetOrCreateStateAsync(context, reserve.Key, cancellationToken);
            state.EditJson = JsonSerializer.Serialize(command, JsonOptions);
            await context.SaveChangesAsync(cancellationToken);
            return true;
        }

        ManualReserveEntity? manual = await context.ManualReserves
            .SingleOrDefaultAsync(item => item.Id == manualId, cancellationToken);
        if (manual is null)
        {
            return false;
        }

        manual.AllowEndLack = command.AllowEndLack;
        manual.Priority = command.Priority;
        manual.IsDeleteOriginalAfterEncode = command.Encode?.IsDeleteOriginalAfterEncode ?? false;
        manual.TagsJson = command.Tags is { Count: > 0 }
            ? JsonSerializer.Serialize(command.Tags, JsonOptions)
            : null;
        manual.ParentDirectoryName = command.Save?.ParentDirectoryName;
        manual.Directory = command.Save?.Directory;
        manual.RecordedFormat = command.Save?.RecordedFormat;
        manual.Mode1 = command.Encode?.Mode1;
        manual.ParentDirectoryName1 = command.Encode?.EncodeParentDirectoryName1;
        manual.Directory1 = command.Encode?.Directory1;
        manual.Mode2 = command.Encode?.Mode2;
        manual.ParentDirectoryName2 = command.Encode?.EncodeParentDirectoryName2;
        manual.Directory2 = command.Encode?.Directory2;
        manual.Mode3 = command.Encode?.Mode3;
        manual.ParentDirectoryName3 = command.Encode?.EncodeParentDirectoryName3;
        manual.Directory3 = command.Encode?.Directory3;

        await context.SaveChangesAsync(cancellationToken);
        return true;
    }

    private async Task<ReserveStateEntity> GetOrCreateStateAsync(
        EpgDbContext context,
        string key,
        CancellationToken cancellationToken)
    {
        ReserveStateEntity? state = await context.ReserveStates
            .SingleOrDefaultAsync(item => item.Key == key, cancellationToken);
        if (state is not null)
        {
            return state;
        }

        state = new ReserveStateEntity { Key = key, CreatedAt = timeProvider.GetUtcNow() };
        context.ReserveStates.Add(state);
        return state;
    }

    public async ValueTask<IReadOnlyList<ManualReserve>> ListManualReservesAsync(
        CancellationToken cancellationToken)
    {
        await using EpgDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);
        ManualReserveEntity[] entities = await context.ManualReserves.AsNoTracking()
            .OrderBy(item => item.Id)
            .ToArrayAsync(cancellationToken);

        // 物理チャンネル (Channel) はチューナーの相乗り判定に要る。予約行には持たせず、
        // その時点のチャンネル一覧から引く — 局側の値が動いても常に最新を見る。
        Dictionary<long, string> channelsById = await context.Channels.AsNoTracking()
            .ToDictionaryAsync(channel => channel.Id, channel => channel.Channel, cancellationToken);

        return [.. entities.Select(item => new ManualReserve(
            item.Id,
            item.ChannelId,
            item.ChannelType,
            item.StartAt,
            item.EndAt,
            item.Name,
            item.ProgramId,
            item.IsTimeSpecified,
            Priority: item.Priority,
            Channel: channelsById.GetValueOrDefault(item.ChannelId, string.Empty)))];
    }

    public async ValueTask<IReadOnlyList<StoredReserve>> ListStoredAsync(CancellationToken cancellationToken)
    {
        await using EpgDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);
        ReserveEntity[] entities = await context.Reserves.AsNoTracking()
            .OrderBy(item => item.Id)
            .ToArrayAsync(cancellationToken);

        return [.. entities.Select(item => new StoredReserve(
            item.Key,
            item.Id,
            item.ProgramId,
            item.ChannelId,
            item.ChannelType,
            item.StartAt,
            item.EndAt,
            item.Name,
            item.HalfWidthName,
            item.IsSkip))];
    }

    public async ValueTask<ReserveStates> ListStatesAsync(CancellationToken cancellationToken)
    {
        await using EpgDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);
        ReserveStateEntity[] states = await context.ReserveStates.AsNoTracking()
            .ToArrayAsync(cancellationToken);

        return new ReserveStates(
            states.Where(state => state.IsSkip).Select(state => state.Key).ToHashSet(StringComparer.Ordinal),
            states.Where(state => state.IsOverlapCleared).Select(state => state.Key)
                .ToHashSet(StringComparer.Ordinal));
    }

    /// <summary>
    /// <see cref="ReserveEntity.Key"/> で差分更新する。全消しだと予約 id が変わって外から追えない。
    /// </summary>
    public async ValueTask ReplaceAsync(
        IReadOnlyList<ReserveAssignment> assignments,
        DateTimeOffset generatedAt,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(assignments);

        await using EpgDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        Dictionary<string, ReserveAssignment> incoming = assignments.ToDictionary(
            assignment => assignment.Target.Key,
            StringComparer.Ordinal);
        ReserveEntity[] stored = await context.Reserves.ToArrayAsync(cancellationToken);

        foreach (ReserveEntity entity in stored)
        {
            if (!incoming.Remove(entity.Key, out ReserveAssignment? assignment))
            {
                // 生成結果に無い予約。番組表から消えたか、ルールが変わった。
                context.Reserves.Remove(entity);
                continue;
            }

            Apply(entity, assignment, generatedAt);
        }

        context.Reserves.AddRange(
            incoming.Values.Select(assignment => Apply(new ReserveEntity(), assignment, generatedAt)));

        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    /// <summary>
    /// 生成結果を行へ写す。id には触れない — 触ると予約 id が入れ替わる。
    /// </summary>
    private static ReserveEntity Apply(
        ReserveEntity entity,
        ReserveAssignment assignment,
        DateTimeOffset generatedAt)
    {
        ReserveTarget target = assignment.Target;
        entity.Key = target.Key;
        entity.Source = target.Source.ToString();
        entity.RuleId = target.RuleId;
        entity.ProgramId = target.ProgramId;
        entity.ManualReserveId = target.ManualReserveId;
        entity.ChannelId = target.ChannelId;
        entity.ChannelType = target.ChannelType;
        entity.StartAt = target.StartAt;
        entity.EndAt = target.EndAt;
        entity.Name = target.Name;
        entity.HalfWidthName = target.Name;
        entity.Priority = target.Priority;
        entity.IsSkip = target.IsSkip;
        entity.IsConflict = assignment.IsConflict;
        entity.IsOverlap = target.IsOverlap;
        entity.TunerIndex = assignment.TunerIndex;
        entity.GeneratedAt = generatedAt;
        return entity;
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
        long[] ruleIds = [.. reserves.Where(item => item.RuleId is not null)
            .Select(item => item.RuleId!.Value)
            .Distinct()];

        Dictionary<long, EpgProgramEntity> programs = await context.Programs.AsNoTracking()
            .Where(program => programIds.Contains(program.Id))
            .ToDictionaryAsync(program => program.Id, cancellationToken);
        Dictionary<long, ManualReserveEntity> manuals = await context.ManualReserves.AsNoTracking()
            .Where(manual => manualIds.Contains(manual.Id))
            .ToDictionaryAsync(manual => manual.Id, cancellationToken);
        RuleEntity[] ruleEntities = await context.Rules.AsNoTracking()
            .Where(rule => ruleIds.Contains(rule.Id))
            .ToArrayAsync(cancellationToken);
        Dictionary<long, RecordingRule> rules = ruleEntities.ToDictionary(
            rule => rule.Id,
            rule => rule.ToDomain());
        string[] reserveKeys = [.. reserves.Select(reserve => reserve.Key)];
        ReserveStateEntity[] editedStates = await context.ReserveStates.AsNoTracking()
            .Where(state => reserveKeys.Contains(state.Key) && state.EditJson != null)
            .ToArrayAsync(cancellationToken);
        Dictionary<string, CreateReserveCommand> edits = editedStates.ToDictionary(
            state => state.Key,
            state => JsonSerializer.Deserialize<CreateReserveCommand>(state.EditJson!, JsonOptions)!,
            StringComparer.Ordinal);

        return [.. reserves.Select(reserve => reserve.ToDomain(
            reserve.ProgramId is { } programId && programs.TryGetValue(programId, out EpgProgramEntity? program)
                ? program
                : null,
            reserve.ManualReserveId is { } manualId && manuals.TryGetValue(manualId, out ManualReserveEntity? manual)
                ? manual
                : null,
            edits.GetValueOrDefault(reserve.Key),
            reserve.RuleId is { } ruleId && rules.TryGetValue(ruleId, out RecordingRule? rule)
                ? rule
                : null))];
    }
}
