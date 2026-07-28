using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TNLAStation.Application.Abstractions;
using TNLAStation.Infrastructure.Configuration;
using TNLAStation.Infrastructure.Persistence;

namespace TNLAStation.Infrastructure.Streaming;

public sealed partial class RemoteThumbnailService(
    HttpClient httpClient,
    IDbContextFactory<EpgDbContext> contextFactory,
    IVideoFileRepository videoFiles,
    IOptions<ThumbnailOptions> thumbnailOptions,
    TimeProvider timeProvider,
    ILogger<RemoteThumbnailService> logger) : IThumbnailService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ThumbnailOptions options = thumbnailOptions.Value;

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
        using HttpResponseMessage response = await httpClient.PostAsJsonAsync(
            "thumbnail",
            new ThumbnailRequest(input, output, options.Width, options.Height, options.PositionSeconds, options.Command),
            JsonOptions,
            cancellationToken);
        response.EnsureSuccessStatusCode();
        ThumbnailResponse? result = await response.Content.ReadFromJsonAsync<ThumbnailResponse>(JsonOptions, cancellationToken);
        if (result is { Success: true })
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
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private sealed record ThumbnailRequest(string InputPath, string OutputPath, int Width, int? Height, double PositionSeconds, string? Command);

    private sealed record ThumbnailResponse(bool Success);

    [LoggerMessage(
        EventId = 6000,
        Level = LogLevel.Warning,
        Message = "Could not take a thumbnail from {Path}")]
    private static partial void LogThumbnailFailed(ILogger logger, string path);
}
