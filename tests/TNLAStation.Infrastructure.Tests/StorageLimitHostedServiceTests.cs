using System.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TNLAStation.Application.Abstractions;
using TNLAStation.Application.Models;
using TNLAStation.Domain;
using TNLAStation.Infrastructure.Configuration;
using TNLAStation.Infrastructure.Storage;

namespace TNLAStation.Infrastructure.Tests;

public sealed class StorageLimitHostedServiceTests
{
    [Fact]
    public async Task DoesNothingWhenFreeSpaceIsAboveTheThreshold()
    {
        var recorded = new FakeRecordedRepository([]);
        var items = new FakeRecordedItemRepository();
        var service = new StorageLimitHostedService(
            Options.Create(new StorageOptions()),
            recorded,
            items,
            TimeProvider.System,
            NullLogger<StorageLimitHostedService>.Instance,
            freeBytesLookup: _ => 10_000L * 1024 * 1024);

        var directory = new RecordedDirectoryOptions
        {
            Name = "recorded",
            Path = "/recorded",
            LimitThresholdMb = 500,
            Action = "remove",
        };
        await service.CheckAsync(directory, CancellationToken.None);

        Assert.Empty(items.Deleted);
        Assert.Equal(0, recorded.Lookups);
    }

    [Fact]
    public async Task DeletesOldestUnprotectedRecordingsUntilAboveTheThreshold()
    {
        long free = 100L * 1024 * 1024;
        var recorded = new FakeRecordedRepository([10, 11, 12]);
        var items = new FakeRecordedItemRepository { OnDelete = _ => free += 300L * 1024 * 1024 };
        var service = new StorageLimitHostedService(
            Options.Create(new StorageOptions()),
            recorded,
            items,
            TimeProvider.System,
            NullLogger<StorageLimitHostedService>.Instance,
            freeBytesLookup: _ => free);

        var directory = new RecordedDirectoryOptions
        {
            Name = "recorded",
            Path = "/recorded",
            LimitThresholdMb = 500,
            Action = "remove",
        };
        await service.CheckAsync(directory, CancellationToken.None);

        Assert.Equal([10L, 11L], items.Deleted);
    }

    [Fact]
    public async Task StopsWhenNoMoreUnprotectedRecordingsExist()
    {
        var recorded = new FakeRecordedRepository([]);
        var items = new FakeRecordedItemRepository();
        var service = new StorageLimitHostedService(
            Options.Create(new StorageOptions()),
            recorded,
            items,
            TimeProvider.System,
            NullLogger<StorageLimitHostedService>.Instance,
            freeBytesLookup: _ => 0L);

        var directory = new RecordedDirectoryOptions
        {
            Name = "recorded",
            Path = "/recorded",
            LimitThresholdMb = 500,
            Action = "remove",
        };
        await service.CheckAsync(directory, CancellationToken.None);

        Assert.Empty(items.Deleted);
    }

    [Fact]
    public async Task DoesNotDeleteWhenActionIsNotRemoveEvenBelowTheThreshold()
    {
        var recorded = new FakeRecordedRepository([10]);
        var items = new FakeRecordedItemRepository();
        var service = new StorageLimitHostedService(
            Options.Create(new StorageOptions()),
            recorded,
            items,
            TimeProvider.System,
            NullLogger<StorageLimitHostedService>.Instance,
            freeBytesLookup: _ => 0L);

        var directory = new RecordedDirectoryOptions
        {
            Name = "recorded",
            Path = "/recorded",
            LimitThresholdMb = 500,
            Action = null,
        };
        await service.CheckAsync(directory, CancellationToken.None);

        Assert.Empty(items.Deleted);
        Assert.Equal(0, recorded.Lookups);
    }

    [Fact]
    public async Task RunsTheLimitCommandWhenBelowTheThreshold()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        string directory = Path.Combine(Path.GetTempPath(), $"tnla-storage-limit-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string outputPath = Path.Combine(directory, "ran.txt");
        string script = CreateTouchScript(directory, outputPath);

        try
        {
            var recorded = new FakeRecordedRepository([]);
            var items = new FakeRecordedItemRepository();
            var service = new StorageLimitHostedService(
                Options.Create(new StorageOptions()),
                recorded,
                items,
                TimeProvider.System,
                NullLogger<StorageLimitHostedService>.Instance,
                freeBytesLookup: _ => 0L);

            var options = new RecordedDirectoryOptions
            {
                Name = "recorded",
                Path = directory,
                LimitThresholdMb = 500,
                Action = null,
                LimitCmd = script,
            };
            await service.CheckAsync(options, CancellationToken.None);

            long startedAt = Stopwatch.GetTimestamp();
            while (!File.Exists(outputPath))
            {
                if (Stopwatch.GetElapsedTime(startedAt) >= TimeSpan.FromSeconds(10))
                {
                    throw new TimeoutException("The limit command did not run in time.");
                }

                await Task.Delay(20);
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [System.Runtime.Versioning.SupportedOSPlatform("linux")]
    private static string CreateTouchScript(string directory, string outputPath)
    {
        string script = Path.Combine(directory, "touch-ran");
        File.WriteAllText(script, $"#!/bin/sh\n: > \"{outputPath}\"\n");
        File.SetUnixFileMode(
            script,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return script;
    }

    private sealed class FakeRecordedRepository(IEnumerable<long> oldestIds) : IRecordedRepository
    {
        private readonly Queue<long> oldestIds = new(oldestIds);

        public int Lookups { get; private set; }

        public ValueTask<long?> FindOldestUnprotectedAsync(CancellationToken cancellationToken)
        {
            Lookups++;
            return ValueTask.FromResult(oldestIds.Count > 0 ? oldestIds.Dequeue() : (long?)null);
        }

        public ValueTask<Page<RecordedProgram>> ListAsync(RecordedQuery query, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public ValueTask<long> AddAsync(CreateRecordedCommand command, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class FakeRecordedItemRepository : IRecordedItemRepository
    {
        public List<long> Deleted { get; } = [];

        public Action<long>? OnDelete { get; set; }

        public ValueTask<bool> DeleteAsync(long recordedId, CancellationToken cancellationToken)
        {
            Deleted.Add(recordedId);
            OnDelete?.Invoke(recordedId);
            return ValueTask.FromResult(true);
        }

        public ValueTask<RecordedProgram?> GetAsync(long recordedId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public ValueTask<bool> SetProtectedAsync(long recordedId, bool isProtected, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public ValueTask<RecordedCleanupResult> CleanupAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
