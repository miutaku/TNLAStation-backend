using Microsoft.Extensions.Options;
using TNLAStation.Application.Abstractions;
using TNLAStation.Domain;
using TNLAStation.Infrastructure.Configuration;

namespace TNLAStation.Infrastructure.Repositories;

/// <summary>
/// Reports the disk usage of every configured recording destination. EPGStation derives the same
/// numbers from statvfs, so <c>used</c> is the space the filesystem reports as occupied rather than
/// the difference between the total size and the space available to an unprivileged writer.
/// </summary>
public sealed class RecordedDirectoryStorageRepository(IOptions<StorageOptions> options) : IStorageRepository
{
    private readonly StorageOptions storageOptions = options.Value;

    public ValueTask<IReadOnlyList<StorageUsage>> ListAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var items = new List<StorageUsage>(storageOptions.RecordedDirectories.Count);
        foreach (RecordedDirectoryOptions directory in storageOptions.RecordedDirectories)
        {
            var drive = new DriveInfo(directory.Path);
            items.Add(new StorageUsage(
                directory.Name,
                Available: drive.AvailableFreeSpace,
                Used: drive.TotalSize - drive.TotalFreeSpace,
                Total: drive.TotalSize));
        }

        return ValueTask.FromResult<IReadOnlyList<StorageUsage>>(items);
    }
}
