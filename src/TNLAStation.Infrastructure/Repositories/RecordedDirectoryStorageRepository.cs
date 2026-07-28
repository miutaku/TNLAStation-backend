using System.Security;
using Microsoft.Extensions.Options;
using TNLAStation.Application.Abstractions;
using TNLAStation.Domain;
using TNLAStation.Infrastructure.Configuration;

namespace TNLAStation.Infrastructure.Repositories;

/// <summary>
/// Reports the disk usage of every configured recording destination. Managed files are identified
/// only when TNLAStation knows how they are used. Everything else, including filesystem usage
/// outside the configured directory and space unavailable to the application, is reported as
/// <c>other</c>.
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
            long available = Math.Clamp(drive.AvailableFreeSpace, 0, drive.TotalSize);
            long used = drive.TotalSize - available;
            StorageFileUsage[] fileTypes = GetFileTypes(directory.Path, cancellationToken);
            fileTypes = IncludeUnmanagedUsage(fileTypes, used);

            items.Add(new StorageUsage(
                directory.Name,
                Available: available,
                Used: used,
                Total: drive.TotalSize,
                FileTypes: fileTypes));
        }

        return ValueTask.FromResult<IReadOnlyList<StorageUsage>>(items);
    }

    private static StorageFileUsage[] IncludeUnmanagedUsage(
        IReadOnlyList<StorageFileUsage> fileTypes,
        long used)
    {
        StorageFileUsage[] managed = fileTypes
            .Where(item => item.Category != "other")
            .ToArray();
        long managedSize = managed.Aggregate(
            0L,
            (total, item) => total > long.MaxValue - item.Size ? long.MaxValue : total + item.Size);
        long unmanagedSize = Math.Max(0, used - managedSize);
        long unmanagedCount = fileTypes
            .Where(item => item.Category == "other")
            .Sum(item => item.Count);

        return
        [
            .. managed,
            new StorageFileUsage(
                Category: "other",
                Format: "other",
                Count: unmanagedCount,
                Size: unmanagedSize),
        ];
    }

    private static StorageFileUsage[] GetFileTypes(
        string rootDirectory,
        CancellationToken cancellationToken)
    {
        var usage = new Dictionary<StorageFileType, MutableFileUsage>();

        foreach (string filename in EnumerateFiles(rootDirectory, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var file = new FileInfo(filename);
                if (!file.Exists || file.Attributes.HasFlag(FileAttributes.ReparsePoint))
                {
                    continue;
                }

                StorageFileType type = StorageFileClassifier.Classify(file.Name);
                if (!usage.TryGetValue(type, out MutableFileUsage? aggregate))
                {
                    aggregate = new MutableFileUsage();
                    usage.Add(type, aggregate);
                }

                aggregate.Count++;
                aggregate.Size += file.Length;
            }
            catch (Exception exception) when (IsRecoverableFileSystemException(exception))
            {
                // Recordings may be created, moved, or deleted while this snapshot is collected.
                // A single transient file must not make the entire storage page unavailable.
            }
        }

        return usage
            .Select(item => new StorageFileUsage(
                item.Key.Category,
                item.Key.Format,
                item.Value.Count,
                item.Value.Size))
            .OrderByDescending(item => item.Size)
            .ThenBy(item => item.Category, StringComparer.Ordinal)
            .ThenBy(item => item.Format, StringComparer.Ordinal)
            .ToArray();
    }

    private static IEnumerable<string> EnumerateFiles(
        string rootDirectory,
        CancellationToken cancellationToken)
    {
        var directories = new Stack<string>();
        directories.Push(rootDirectory);

        while (directories.TryPop(out string? currentDirectory))
        {
            cancellationToken.ThrowIfCancellationRequested();

            string[] files;
            try
            {
                files = Directory.GetFiles(currentDirectory);
            }
            catch (Exception exception) when (IsRecoverableFileSystemException(exception))
            {
                continue;
            }

            foreach (string file in files)
            {
                yield return file;
            }

            string[] children;
            try
            {
                children = Directory.GetDirectories(currentDirectory);
            }
            catch (Exception exception) when (IsRecoverableFileSystemException(exception))
            {
                continue;
            }

            foreach (string child in children)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    if (!File.GetAttributes(child).HasFlag(FileAttributes.ReparsePoint))
                    {
                        directories.Push(child);
                    }
                }
                catch (Exception exception) when (IsRecoverableFileSystemException(exception))
                {
                    // The directory disappeared or became inaccessible during enumeration.
                }
            }
        }
    }

    private static bool IsRecoverableFileSystemException(Exception exception) =>
        exception is IOException
            or UnauthorizedAccessException
            or SecurityException;

    private sealed class MutableFileUsage
    {
        public long Count { get; set; }

        public long Size { get; set; }
    }
}

internal readonly record struct StorageFileType(string Category, string Format);

internal static class StorageFileClassifier
{
    private static readonly Dictionary<string, StorageFileType> ExtensionTypes =
        new Dictionary<string, StorageFileType>(StringComparer.OrdinalIgnoreCase)
        {
            [".ts"] = new("video", "mpeg-ts"),
            [".m2ts"] = new("video", "mpeg-ts"),
            [".mp4"] = new("video", "mp4"),
            [".mkv"] = new("video", "matroska"),
            [".webm"] = new("video", "webm"),
            [".jpg"] = new("image", "jpeg"),
            [".jpeg"] = new("image", "jpeg"),
        };

    public static StorageFileType Classify(string filename)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filename);

        if (filename.EndsWith(".drop.log", StringComparison.OrdinalIgnoreCase))
        {
            return new StorageFileType("log", "drop-log");
        }

        if (filename.EndsWith(".encode.log", StringComparison.OrdinalIgnoreCase))
        {
            return new StorageFileType("log", "encode-log");
        }

        return ExtensionTypes.TryGetValue(Path.GetExtension(filename), out StorageFileType type)
            ? type
            : new StorageFileType("other", "other");
    }
}
