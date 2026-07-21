using Microsoft.EntityFrameworkCore;
using TNLAStation.Application.Abstractions;
using TNLAStation.Application.Models;
using TNLAStation.Domain;
using TNLAStation.Infrastructure.Persistence;

namespace TNLAStation.Infrastructure.Repositories;

/// <summary>
/// 録画済みと録画中。同じ表の、録画中かどうかの違いでしかないので 1 つの実装で扱う。
/// </summary>
public sealed class PostgresRecordedRepository(
    IDbContextFactory<EpgDbContext> contextFactory,
    TimeProvider timeProvider) : IRecordedRepository, IRecordingRepository, IRecordedTagRepository, IRecordedItemRepository, IRecordedTagWriteRepository
{
    public ValueTask<Page<RecordedProgram>> ListAsync(RecordedQuery query, CancellationToken cancellationToken) =>
        ListAsync(query, onlyRecording: false, cancellationToken);

    /// <summary>
    /// 録画中だけを返す。何も録っていない時間のほうが長いので、空は正常な状態。
    /// </summary>
    ValueTask<Page<RecordedProgram>> IRecordingRepository.ListAsync(
        RecordedQuery query,
        CancellationToken cancellationToken) =>
        ListAsync(query, onlyRecording: true, cancellationToken);

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

        if (query.RuleId is { } ruleId)
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
            recorded = recorded.Where(item => EF.Functions.ILike(item.HalfWidthName, $"%{query.Keyword}%"));
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
            // 同上。
        }
    }
}
