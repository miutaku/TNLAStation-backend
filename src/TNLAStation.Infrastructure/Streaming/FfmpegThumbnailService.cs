using System.Diagnostics;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TNLAStation.Application.Abstractions;
using TNLAStation.Infrastructure.Configuration;
using TNLAStation.Infrastructure.Persistence;

namespace TNLAStation.Infrastructure.Streaming;

public sealed partial class FfmpegThumbnailService(
    IDbContextFactory<EpgDbContext> contextFactory,
    IVideoFileRepository videoFiles,
    IMediaProbe probe,
    IOptions<ThumbnailOptions> thumbnailOptions,
    IOptions<StreamingOptions> streamingOptions,
    TimeProvider timeProvider,
    ILogger<FfmpegThumbnailService> logger) : IThumbnailService
{
    private readonly ThumbnailOptions options = thumbnailOptions.Value;
    private readonly StreamingOptions streaming = streamingOptions.Value;

    public async ValueTask<ThumbnailFile?> GetAsync(long thumbnailId, CancellationToken cancellationToken)
    {
        await using EpgDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.Thumbnails.AsNoTracking()
            .Where(thumbnail => thumbnail.Id == thumbnailId)
            .Select(thumbnail => new ThumbnailFile(
                thumbnail.Id,
                thumbnail.RecordedId,
                thumbnail.ParentDirectoryName,
                thumbnail.Filename))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async ValueTask<long?> CreateForVideoFileAsync(long videoFileId, CancellationToken cancellationToken)
    {
        VideoFileLocation? source = await videoFiles.GetAsync(videoFileId, cancellationToken);
        if (source is null || !File.Exists(source.FullPath))
        {
            return null;
        }

        await using EpgDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);
        if (await context.Thumbnails.AnyAsync(item => item.RecordedId == source.RecordedId, cancellationToken))
        {
            return null;
        }

        Directory.CreateDirectory(options.Directory);
        string filename = $"{source.RecordedId.ToString(CultureInfo.InvariantCulture)}.jpg";
        string path = Path.Combine(options.Directory, filename);
        if (!await ExtractAsync(source.FullPath, path, cancellationToken))
        {
            return null;
        }

        var entity = new ThumbnailEntity
        {
            RecordedId = source.RecordedId,
            ParentDirectoryName = options.Directory,
            Filename = filename,
            CreatedAt = timeProvider.GetUtcNow(),
        };
        context.Thumbnails.Add(entity);
        await context.SaveChangesAsync(cancellationToken);
        return entity.Id;
    }

    public async ValueTask<int> CreateMissingAsync(CancellationToken cancellationToken)
    {
        long[] videoFileIds;
        await using (EpgDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken))
        {
            videoFileIds = await context.VideoFiles.AsNoTracking()
                .Where(file => !context.Thumbnails.Any(thumbnail => thumbnail.RecordedId == file.RecordedId))
                .GroupBy(file => file.RecordedId)
                .Select(group => group.Min(file => file.Id))
                .ToArrayAsync(cancellationToken);
        }

        int created = 0;
        foreach (long videoFileId in videoFileIds)
        {
            if (await CreateForVideoFileAsync(videoFileId, cancellationToken) is not null)
            {
                created++;
            }
        }

        return created;
    }

    public async ValueTask<bool> DeleteAsync(long thumbnailId, CancellationToken cancellationToken)
    {
        await using EpgDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);
        ThumbnailEntity? entity = await context.Thumbnails
            .SingleOrDefaultAsync(thumbnail => thumbnail.Id == thumbnailId, cancellationToken);
        if (entity is null)
        {
            return false;
        }

        TryDelete(Path.Combine(entity.ParentDirectoryName, entity.Filename));
        context.Thumbnails.Remove(entity);
        await context.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async ValueTask<int> CleanupAsync(CancellationToken cancellationToken)
    {
        await using EpgDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);
        ThumbnailEntity[] orphans = await context.Thumbnails
            .Where(thumbnail => !context.Recorded.Any(recorded => recorded.Id == thumbnail.RecordedId))
            .ToArrayAsync(cancellationToken);
        foreach (ThumbnailEntity orphan in orphans)
        {
            TryDelete(Path.Combine(orphan.ParentDirectoryName, orphan.Filename));
        }

        context.Thumbnails.RemoveRange(orphans);
        await context.SaveChangesAsync(cancellationToken);
        return orphans.Length;
    }

    private async Task<bool> ExtractAsync(string input, string output, CancellationToken cancellationToken)
    {
        // 先頭は放送の切り替わりで黒いことが多い。少し進めた位置から取る。長さが分かる
        // ときは全体の位置で決め、短い録画で行き過ぎないようにする。
        double? duration = await probe.GetDurationSecondsAsync(input, cancellationToken);
        double position = duration is { } total && total > 0
            ? Math.Min(total * 0.1, Math.Max(0, total - 1))
            : options.PositionSeconds;

        var startInfo = new ProcessStartInfo(streaming.FfmpegPath)
        {
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (string argument in new[]
        {
            "-hide_banner",
            "-loglevel", "error",
            "-ss", position.ToString("0.###", CultureInfo.InvariantCulture),
            "-i", input,
            "-frames:v", "1",
            "-vf", $"yadif,scale={options.Width.ToString(CultureInfo.InvariantCulture)}:-2",
            "-y", output,
        })
        {
            startInfo.ArgumentList.Add(argument);
        }

        using Process? process = Process.Start(startInfo);
        if (process is null)
        {
            return false;
        }

        await process.WaitForExitAsync(cancellationToken);
        if (process.ExitCode == 0 && File.Exists(output))
        {
            return true;
        }

        LogThumbnailFailed(logger, input);
        return false;
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
            // 画像が残っても実害は小さい。
        }
        catch (UnauthorizedAccessException)
        {
            // 同上。
        }
    }

    [LoggerMessage(
        EventId = 6000,
        Level = LogLevel.Warning,
        Message = "Could not take a thumbnail from {Path}")]
    private static partial void LogThumbnailFailed(ILogger logger, string path);
}
