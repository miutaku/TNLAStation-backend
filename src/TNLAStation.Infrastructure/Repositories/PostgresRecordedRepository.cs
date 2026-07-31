using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TNLAStation.Application.Abstractions;
using TNLAStation.Application.Models;
using TNLAStation.Domain;
using TNLAStation.Infrastructure.Configuration;
using TNLAStation.Infrastructure.Persistence;

namespace TNLAStation.Infrastructure.Repositories;

/// <summary>
/// 録画済みと録画中。同じ表の、録画中かどうかの違いでしかないので 1 つの実装で扱う。
/// </summary>
public sealed class PostgresRecordedRepository(
    IDbContextFactory<EpgDbContext> contextFactory,
    TimeProvider timeProvider,
    IOptions<StorageOptions>? storageOptions = null) : IRecordedRepository, IRecordingRepository, IRecordedTagRepository, IRecordedItemRepository, IRecordedTagWriteRepository
{
    private readonly StorageOptions storageOptions = storageOptions?.Value ?? new StorageOptions();

    public ValueTask<Page<RecordedProgram>> ListAsync(RecordedQuery query, CancellationToken cancellationToken) =>
        ListAsync(query, onlyRecording: false, cancellationToken);

    /// <summary>
    /// 録画中だけを返す。何も録っていない時間のほうが長いので、空は正常な状態。
    /// </summary>
    ValueTask<Page<RecordedProgram>> IRecordingRepository.ListAsync(
        RecordedQuery query,
        CancellationToken cancellationToken) =>
        ListAsync(query, onlyRecording: true, cancellationToken);

    /// <summary>
    /// 空き容量不足の削除対象を探す。EPGStation はディレクトリを問わず全体で最古のものを
    /// 消すため、閾値割れした保存先とは別の場所にある録画を消しても空きが増えないことがある。
    /// ここでは意図的に <paramref name="parentDirectoryName"/> に絞って、確実にその保存先の
    /// 空きが増える録画だけを対象にする。録画中のものも対象から外す (EPGStation は外していない)。
    /// </summary>
    public async ValueTask<long?> FindOldestUnprotectedAsync(CancellationToken cancellationToken)
    {
        await using EpgDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);

        // EPGStation (RecordedDB.findOld) は保護状態だけで絞る。保存先も録画中かどうかも見ない。
        // 並びは id 昇順 (EPGStation の 2 度目の orderBy が startAt の指定を上書きするため)。
        return await context.Recorded.AsNoTracking()
            .Where(item => !item.IsProtected)
            .OrderBy(item => item.Id)
            .Select(item => (long?)item.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async ValueTask<long> AddAsync(CreateRecordedCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        await using EpgDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var entity = new RecordedEntity
        {
            ChannelId = command.ChannelId,
            StartAt = DateTimeOffset.FromUnixTimeMilliseconds(command.StartAt),
            EndAt = DateTimeOffset.FromUnixTimeMilliseconds(command.EndAt),
            Name = command.Name,
            HalfWidthName = command.Name,
            Description = command.Description,
            HalfWidthDescription = command.Description,
            Extended = command.Extended,
            HalfWidthExtended = command.Extended,
            RuleId = command.RuleId,
            Genre1 = command.Genre1,
            SubGenre1 = command.SubGenre1,
            Genre2 = command.Genre2,
            SubGenre2 = command.SubGenre2,
            Genre3 = command.Genre3,
            SubGenre3 = command.SubGenre3,
            IsRecording = false,
            CreatedAt = timeProvider.GetUtcNow(),
        };

        context.Recorded.Add(entity);
        await context.SaveChangesAsync(cancellationToken);
        return entity.Id;
    }

    public async ValueTask<RecordedProgram?> GetAsync(long recordedId, CancellationToken cancellationToken)
    {
        await using EpgDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);
        RecordedEntity? entity = await Include(context.Recorded.AsNoTracking())
            .SingleOrDefaultAsync(item => item.Id == recordedId, cancellationToken);
        return entity?.ToDomain(isHalfWidth: false);
    }

    /// <summary>
    /// 録画を消す。ファイルも一緒に消す。行だけ消してファイルを残すと、どこからも辿れない
    /// まま容量を食う。保護されているものは消さない。
    /// </summary>
    public async ValueTask<bool> DeleteAsync(long recordedId, CancellationToken cancellationToken)
    {
        await using EpgDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);
        RecordedEntity? entity = await context.Recorded
            .Include(item => item.VideoFiles)
            .SingleOrDefaultAsync(item => item.Id == recordedId, cancellationToken);
        if (entity is null)
        {
            return false;
        }

        if (entity.IsProtected)
        {
            throw new InvalidOperationException("RecordedIsProtected");
        }

        if (entity.IsRecording)
        {
            throw new InvalidOperationException("RecordedIsRecording");
        }

        foreach (VideoFileEntity file in entity.VideoFiles)
        {
            DeleteFile(file);
        }

        context.Recorded.Remove(entity);
        await context.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async ValueTask<bool> SetProtectedAsync(
        long recordedId,
        bool isProtected,
        CancellationToken cancellationToken)
    {
        await using EpgDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);
        RecordedEntity? entity = await context.Recorded
            .SingleOrDefaultAsync(item => item.Id == recordedId, cancellationToken);
        if (entity is null)
        {
            return false;
        }

        entity.IsProtected = isProtected;
        await context.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <summary>
    /// EPGStation の videoFileCleanup と同じく、両方向のずれを直す:
    /// DB にあるが実ファイルが無いものは行を消し、保存先の下にあるが DB に無いファイル・
    /// 空になったディレクトリは削除する。後者は <see cref="StorageOptions.RecordedDirectories"/>
    /// の配下だけを対象にし、録画中のファイル (VideoFiles に登録済み) と dropLog は必ず除外する。
    /// </summary>
    public async ValueTask<RecordedCleanupResult> CleanupAsync(CancellationToken cancellationToken)
    {
        await using EpgDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);
        RecordedEntity[] candidates = await context.Recorded
            .Include(item => item.VideoFiles)
            .Where(item => !item.IsRecording && !item.IsProtected)
            .ToArrayAsync(cancellationToken);

        RecordedEntity[] gone = [.. candidates.Where(item =>
            item.VideoFiles.Count == 0 ||
            item.VideoFiles.All(file => !File.Exists(Path.Combine(file.ParentDirectoryName, file.Filename))))];

        context.Recorded.RemoveRange(gone);
        await context.SaveChangesAsync(cancellationToken);

        int removedFiles = await CleanupOrphanFilesAsync(cancellationToken);
        return new RecordedCleanupResult(gone.Length, removedFiles);
    }

    private async Task<int> CleanupOrphanFilesAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<string> roots = [.. storageOptions.RecordedDirectories
            .Select(directory => directory.Path)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.Ordinal)];
        if (roots.Count == 0)
        {
            return 0;
        }

        await using EpgDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var known = new HashSet<string>(StringComparer.Ordinal);
        foreach ((string parent, string filename) in await context.VideoFiles.AsNoTracking()
            .Select(file => new ValueTuple<string, string>(file.ParentDirectoryName, file.Filename))
            .ToArrayAsync(cancellationToken))
        {
            known.Add(Path.GetFullPath(Path.Combine(parent, filename)));
        }

        foreach ((string parent, string filename) in await context.DropLogFiles.AsNoTracking()
            .Select(file => new ValueTuple<string, string>(file.ParentDirectoryName, file.Filename))
            .ToArrayAsync(cancellationToken))
        {
            known.Add(Path.GetFullPath(Path.Combine(parent, filename)));
        }

        return OrphanFileSweeper.Sweep(roots, known);
    }

    public async ValueTask<Page<RecordedTag>> ListAsync(
        RecordedTagQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        await using EpgDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);
        IQueryable<RecordedTagEntity> tags = context.RecordedTags.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(query.Name))
        {
            tags = tags.Where(tag => EF.Functions.ILike(tag.Name, $"%{query.Name}%"));
        }

        if (query.ExcludeTagIds is { Count: > 0 })
        {
            tags = tags.Where(tag => !query.ExcludeTagIds.Contains(tag.Id));
        }

        tags = tags.OrderBy(tag => tag.Name);
        int total = await tags.CountAsync(cancellationToken);
        if (query.Offset is { } offset)
        {
            tags = tags.Skip(offset);
        }

        if (query.Limit is { } limit)
        {
            tags = tags.Take(limit);
        }

        RecordedTagEntity[] items = await tags.ToArrayAsync(cancellationToken);
        return new Page<RecordedTag>(
            [.. items.Select(tag => new RecordedTag(tag.Id, tag.Name, tag.Color))],
            total);
    }

    public async ValueTask<long> AddTagAsync(string name, string color, CancellationToken cancellationToken)
    {
        await using EpgDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var entity = new RecordedTagEntity { Name = name, Color = color };
        context.RecordedTags.Add(entity);
        await context.SaveChangesAsync(cancellationToken);
        return entity.Id;
    }

    public async ValueTask<bool> UpdateTagAsync(
        long tagId,
        string name,
        string color,
        CancellationToken cancellationToken)
    {
        await using EpgDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);
        RecordedTagEntity? entity = await context.RecordedTags
            .SingleOrDefaultAsync(tag => tag.Id == tagId, cancellationToken);
        if (entity is null)
        {
            return false;
        }

        entity.Name = name;
        entity.Color = color;
        await context.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async ValueTask<bool> DeleteTagAsync(long tagId, CancellationToken cancellationToken)
    {
        await using EpgDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);
        int removed = await context.RecordedTags
            .Where(tag => tag.Id == tagId)
            .ExecuteDeleteAsync(cancellationToken);
        return removed > 0;
    }

    public async ValueTask<bool> SetTagAsync(
        long recordedId,
        long tagId,
        bool attached,
        CancellationToken cancellationToken)
    {
        await using EpgDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);
        bool exists = await context.Recorded.AnyAsync(item => item.Id == recordedId, cancellationToken) &&
            await context.RecordedTags.AnyAsync(tag => tag.Id == tagId, cancellationToken);
        if (!exists)
        {
            return false;
        }

        RecordedTagLinkEntity? link = await context.RecordedTagLinks
            .SingleOrDefaultAsync(
                item => item.RecordedId == recordedId && item.TagId == tagId,
                cancellationToken);

        if (attached && link is null)
        {
            context.RecordedTagLinks.Add(new RecordedTagLinkEntity { RecordedId = recordedId, TagId = tagId });
        }
        else if (!attached && link is not null)
        {
            context.RecordedTagLinks.Remove(link);
        }

        await context.SaveChangesAsync(cancellationToken);
        return true;
    }

    private async ValueTask<Page<RecordedProgram>> ListAsync(
        RecordedQuery query,
        bool onlyRecording,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        await using EpgDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);
        IQueryable<RecordedEntity> recorded = context.Recorded.AsNoTracking()
            .Where(item => item.IsRecording == onlyRecording);

        if (query.RuleId == 0)
        {
            recorded = recorded.Where(item => item.RuleId == null);
        }
        else if (query.RuleId is { } ruleId)
        {
            recorded = recorded.Where(item => item.RuleId == ruleId);
        }

        if (query.ChannelId is { } channelId)
        {
            recorded = recorded.Where(item => item.ChannelId == channelId);
        }

        if (query.Genre is { } genre)
        {
            recorded = recorded.Where(item =>
                item.Genre1 == genre || item.Genre2 == genre || item.Genre3 == genre);
        }

        if (!string.IsNullOrWhiteSpace(query.Keyword))
        {
            string pattern = $"%{query.Keyword.Trim()}%";
            recorded = recorded.Where(item =>
                EF.Functions.ILike(item.Name, pattern) ||
                EF.Functions.ILike(item.HalfWidthName, pattern) ||
                (item.Description != null && EF.Functions.ILike(item.Description, pattern)) ||
                (item.HalfWidthDescription != null && EF.Functions.ILike(item.HalfWidthDescription, pattern)) ||
                (item.Extended != null && EF.Functions.ILike(item.Extended, pattern)) ||
                (item.HalfWidthExtended != null && EF.Functions.ILike(item.HalfWidthExtended, pattern)));
        }

        if (query.HasOriginalFile == true)
        {
            recorded = recorded.Where(item => item.VideoFiles.Any(file => file.Type == "ts"));
        }

        int total = await recorded.CountAsync(cancellationToken);
        // 既定は新しい順。録画は増え続けるので、探しているものはたいてい最近のもの。
        recorded = query.IsReverse == true
            ? recorded.OrderBy(item => item.StartAt).ThenBy(item => item.Id)
            : recorded.OrderByDescending(item => item.StartAt).ThenByDescending(item => item.Id);

        RecordedEntity[] items = await Include(recorded)
            .Skip(query.Offset)
            .Take(query.Limit)
            .ToArrayAsync(cancellationToken);

        return new Page<RecordedProgram>(
            [.. items.Select(item => item.ToDomain(query.IsHalfWidth))],
            total);
    }

    private static IQueryable<RecordedEntity> Include(IQueryable<RecordedEntity> recorded) =>
        recorded
            .Include(item => item.VideoFiles)
            .Include(item => item.Thumbnails)
            .Include(item => item.DropLogFile)
            .Include(item => item.TagLinks)
            .ThenInclude(link => link.Tag);

    private static void DeleteFile(VideoFileEntity file)
    {
        try
        {
            string path = Path.Combine(file.ParentDirectoryName, file.Filename);
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // 消せなくても行は消す。残った行から辿れないファイルより、残ったファイルのほうがまだ扱える。
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
