using Microsoft.EntityFrameworkCore;
using TNLAStation.Application.Abstractions;
using TNLAStation.Infrastructure.Persistence;

namespace TNLAStation.Infrastructure.Repositories;

public sealed class PostgresVideoFileRepository(IDbContextFactory<EpgDbContext> contextFactory)
    : IVideoFileRepository
{
    public async ValueTask<VideoFileLocation?> GetAsync(long videoFileId, CancellationToken cancellationToken)
    {
        await using EpgDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.VideoFiles.AsNoTracking()
            .Where(file => file.Id == videoFileId)
            .Select(file => new VideoFileLocation(
                file.Id,
                file.RecordedId,
                file.Name,
                file.ParentDirectoryName,
                file.Filename,
                file.Type,
                file.Size))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async ValueTask<bool> DeleteAsync(long videoFileId, CancellationToken cancellationToken)
    {
        await using EpgDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);
        VideoFileEntity? file = await context.VideoFiles
            .SingleOrDefaultAsync(item => item.Id == videoFileId, cancellationToken);
        if (file is null)
        {
            return false;
        }

        RecordedEntity? recorded = await context.Recorded
            .SingleOrDefaultAsync(item => item.Id == file.RecordedId, cancellationToken);
        if (recorded is { IsProtected: true })
        {
            throw new InvalidOperationException("RecordedIsProtected");
        }

        if (recorded is { IsRecording: true })
        {
            throw new InvalidOperationException("RecordedIsRecording");
        }

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
            // 消せなくても行は消す。辿れないファイルより、残ったファイルのほうがまだ扱える。
        }
        catch (UnauthorizedAccessException)
        {
            // 同上。
        }

        context.VideoFiles.Remove(file);
        await context.SaveChangesAsync(cancellationToken);
        return true;
    }
}
