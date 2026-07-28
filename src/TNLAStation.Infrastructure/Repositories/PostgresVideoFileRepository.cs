using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TNLAStation.Application.Abstractions;
using TNLAStation.Infrastructure.Configuration;
using TNLAStation.Infrastructure.Persistence;

namespace TNLAStation.Infrastructure.Repositories;

public sealed class PostgresVideoFileRepository(
    IDbContextFactory<EpgDbContext> contextFactory,
    IOptions<StorageOptions> storageOptions,
    TimeProvider timeProvider) : IVideoFileRepository, IVideoFileUploadRepository
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

    public async ValueTask<long?> UploadAsync(
        VideoFileUpload upload,
        Stream content,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(upload);

        await using EpgDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);
        if (!await context.Recorded.AnyAsync(item => item.Id == upload.RecordedId, cancellationToken))
        {
            return null;
        }

        string? parent = ResolveDirectory(upload.ParentDirectoryName);
        if (parent is null)
        {
            throw new InvalidOperationException("ParentDirectoryIsNotFound");
        }

        string directory = string.IsNullOrWhiteSpace(upload.SubDirectory)
            ? parent
            : Path.Combine(parent, upload.SubDirectory);
        Directory.CreateDirectory(directory);

        // 名前がぶつかると先にあったものを上書きしてしまう。
        string filename = Path.GetFileName(upload.OriginalFileName);
        string baseName = Path.GetFileNameWithoutExtension(filename);
        string extension = Path.GetExtension(filename);
        for (int suffix = 1; File.Exists(Path.Combine(directory, filename)); suffix++)
        {
            filename = $"{baseName}-{suffix}{extension}";
        }

        string path = Path.Combine(directory, filename);
        string writePath = path;
        string? tempDirectory = storageOptions.Value.UploadTempDirectory;
        if (!string.IsNullOrWhiteSpace(tempDirectory))
        {
            Directory.CreateDirectory(tempDirectory);
            writePath = Path.Combine(tempDirectory, $"{Guid.NewGuid():N}{extension}");
        }

        try
        {
            await using (var destination = new FileStream(writePath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await content.CopyToAsync(destination, cancellationToken);
            }

            if (writePath != path)
            {
                File.Move(writePath, path);
            }
        }
        catch
        {
            TryDelete(writePath);
            throw;
        }

        var entity = new VideoFileEntity
        {
            RecordedId = upload.RecordedId,
            Name = upload.Name,
            Filename = filename,
            ParentDirectoryName = directory,
            Type = upload.Type == "ts" ? "ts" : "encoded",
            Size = new FileInfo(path).Length,
            CreatedAt = timeProvider.GetUtcNow(),
        };
        context.VideoFiles.Add(entity);
        await context.SaveChangesAsync(cancellationToken);
        return entity.Id;
    }

    /// <summary>
    /// 保存先は名前で指定される。任意のパスを受け取ると、設定した場所の外へ書ける。
    /// </summary>
    private string? ResolveDirectory(string name)
    {
        IReadOnlyList<RecordedDirectoryOptions> directories = storageOptions.Value.RecordedDirectories;
        RecordedDirectoryOptions? match = directories.FirstOrDefault(
            directory => string.Equals(directory.Name, name, StringComparison.Ordinal));
        return match?.Path ?? (directories.Count > 0 && string.IsNullOrWhiteSpace(name) ? directories[0].Path : null);
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
        }

        context.VideoFiles.Remove(file);
        await context.SaveChangesAsync(cancellationToken);
        return true;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // 消せなくても、アップロード自体の失敗は呼び出し元へ伝える。
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
