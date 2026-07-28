using Microsoft.EntityFrameworkCore;
using TNLAStation.Application.Abstractions;
using TNLAStation.Domain;
using TNLAStation.Infrastructure.Persistence;

namespace TNLAStation.Infrastructure.Repositories;

public sealed class PostgresRecordingStore(
    IDbContextFactory<EpgDbContext> contextFactory,
    TimeProvider timeProvider) : IRecordingStore, IDropLogRepository
{
    public async ValueTask<(long RecordedId, long VideoFileId)> BeginAsync(
        RecordingStart start,
        string parentDirectoryName,
        string filename,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(start);

        await using EpgDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);
        DateTimeOffset now = timeProvider.GetUtcNow();
        var recorded = new RecordedEntity
        {
            ProgramId = start.ProgramId,
            RuleId = start.RuleId,
            ReserveId = start.ReserveId,
            ReserveKey = start.ReserveKey,
            ManualReserveId = start.ManualReserveId,
            ChannelId = start.ChannelId,
            StartAt = start.StartAt,
            EndAt = start.EndAt,
            Name = start.Name,
            HalfWidthName = start.HalfWidthName,
            Description = start.Description,
            HalfWidthDescription = start.HalfWidthDescription,
            Extended = start.Extended,
            HalfWidthExtended = start.HalfWidthExtended,
            Genre1 = start.Genre1,
            SubGenre1 = start.SubGenre1,
            Genre2 = start.Genre2,
            SubGenre2 = start.SubGenre2,
            Genre3 = start.Genre3,
            SubGenre3 = start.SubGenre3,
            IsRecording = true,
            CreatedAt = now,
        };
        var file = new VideoFileEntity
        {
            Name = "TS",
            Filename = filename,
            ParentDirectoryName = parentDirectoryName,
            Type = "ts",
            Size = 0,
            CreatedAt = now,
        };
        recorded.VideoFiles.Add(file);

        context.Recorded.Add(recorded);
        await context.SaveChangesAsync(cancellationToken);
        return (recorded.Id, file.Id);
    }

    public async ValueTask CompleteAsync(
        long recordedId,
        long videoFileId,
        long size,
        CancellationToken cancellationToken)
    {
        await using EpgDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await context.Recorded
            .Where(item => item.Id == recordedId)
            .ExecuteUpdateAsync(item => item.SetProperty(x => x.IsRecording, false), cancellationToken);
        await context.VideoFiles
            .Where(item => item.Id == videoFileId)
            .ExecuteUpdateAsync(item => item.SetProperty(x => x.Size, size), cancellationToken);
    }

    public async ValueTask SaveDropLogAsync(
        long recordedId,
        TransportStreamDefects defects,
        string parentDirectoryName,
        string filename,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(defects);

        await using EpgDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);
        if (await context.DropLogFiles.AnyAsync(item => item.RecordedId == recordedId, cancellationToken))
        {
            return;
        }

        context.DropLogFiles.Add(new DropLogFileEntity
        {
            RecordedId = recordedId,
            ParentDirectoryName = parentDirectoryName,
            Filename = filename,
            ErrorCount = defects.ErrorCount,
            DropCount = defects.DropCount,
            ScramblingCount = defects.ScramblingCount,
            CreatedAt = timeProvider.GetUtcNow(),
        });
        await context.SaveChangesAsync(cancellationToken);
    }

    public async ValueTask AbortAsync(long recordedId, CancellationToken cancellationToken)
    {
        await using EpgDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await context.Recorded.Where(item => item.Id == recordedId).ExecuteDeleteAsync(cancellationToken);
    }

    public async ValueTask<DropLogFileLocation?> GetAsync(
        long dropLogFileId,
        CancellationToken cancellationToken)
    {
        await using EpgDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.DropLogFiles.AsNoTracking()
            .Where(item => item.Id == dropLogFileId)
            .Select(item => new DropLogFileLocation(item.Id, item.ParentDirectoryName, item.Filename))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async ValueTask<bool> ExistsAsync(long programId, CancellationToken cancellationToken)
    {
        await using EpgDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.Recorded.AnyAsync(item => item.ProgramId == programId, cancellationToken);
    }

    public async ValueTask<IReadOnlyList<UnfinishedRecording>> ListUnfinishedAsync(
        CancellationToken cancellationToken)
    {
        await using EpgDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.Recorded.AsNoTracking()
            .Where(item => item.IsRecording)
            .SelectMany(item => item.VideoFiles.Select(file => new UnfinishedRecording(
                item.Id,
                file.Id,
                file.ParentDirectoryName,
                file.Filename)))
            .ToArrayAsync(cancellationToken);
    }
}
