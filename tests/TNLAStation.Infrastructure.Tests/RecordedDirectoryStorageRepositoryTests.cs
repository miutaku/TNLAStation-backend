using Microsoft.Extensions.Options;
using TNLAStation.Domain;
using TNLAStation.Infrastructure.Configuration;
using TNLAStation.Infrastructure.Repositories;

namespace TNLAStation.Infrastructure.Tests;

public sealed class RecordedDirectoryStorageRepositoryTests : IDisposable
{
    private readonly string directory =
        Path.Combine(Path.GetTempPath(), $"tnla-storage-{Guid.NewGuid():N}");

    [Fact]
    public async Task ManagedFilesAreAggregatedAndAllRemainingDiskUsageIsOther()
    {
        Directory.CreateDirectory(Path.Combine(directory, "nested"));
        await WriteFileAsync("recording.ts", 10);
        await WriteFileAsync("recording.M2TS", 11);
        await WriteFileAsync("encoded.mp4", 20);
        await WriteFileAsync("thumbnail.jpg", 30);
        await WriteFileAsync("recording.drop.log", 5);
        await WriteFileAsync("recording.encode.log", 6);
        await WriteFileAsync("service.log", 7);
        await WriteFileAsync("unmanaged.mp3", 4);
        await WriteFileAsync("unmanaged.srt", 3);
        await WriteFileAsync("unknown.bin", 8);
        await WriteFileAsync(Path.Combine("nested", "extensionless"), 9);

        var repository = new RecordedDirectoryStorageRepository(Options.Create(new StorageOptions
        {
            RecordedDirectories =
            [
                new RecordedDirectoryOptions { Name = "recorded", Path = directory },
            ],
        }));

        StorageUsage storage = Assert.Single(await repository.ListAsync(CancellationToken.None));
        Dictionary<(string Category, string Format), StorageFileUsage> types =
            storage.FileTypes.ToDictionary(item => (item.Category, item.Format));

        Assert.Equal((2L, 21L), Usage(types, "video", "mpeg-ts"));
        Assert.Equal((1L, 20L), Usage(types, "video", "mp4"));
        Assert.Equal((1L, 30L), Usage(types, "image", "jpeg"));
        Assert.Equal((1L, 5L), Usage(types, "log", "drop-log"));
        Assert.Equal((1L, 6L), Usage(types, "log", "encode-log"));
        Assert.Equal(5L, types[("other", "other")].Count);
        Assert.Equal(storage.Used - 82, types[("other", "other")].Size);
        Assert.Equal(storage.Used, storage.FileTypes.Sum(item => item.Size));
        Assert.Equal(storage.Total, storage.Used + storage.Available);
    }

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static (long Count, long Size) Usage(
        Dictionary<(string Category, string Format), StorageFileUsage> types,
        string category,
        string format)
    {
        StorageFileUsage usage = types[(category, format)];
        return (usage.Count, usage.Size);
    }

    private async Task WriteFileAsync(string relativePath, int length)
    {
        string path = Path.Combine(directory, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllBytesAsync(path, new byte[length]);
    }
}
