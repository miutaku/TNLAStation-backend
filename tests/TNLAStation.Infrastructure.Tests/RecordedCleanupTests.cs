using Microsoft.Extensions.Options;
using TNLAStation.Application.Abstractions;
using TNLAStation.Infrastructure.Configuration;
using TNLAStation.Infrastructure.Repositories;

namespace TNLAStation.Infrastructure.Tests;

/// <summary>
/// 録画の片付け (EPGStation の videoFileCleanup 相当)。DB に無い実ファイルを消す機能は
/// 一歩間違えると録画済みファイルを消してしまうので、守るべきものが確実に守られることを
/// 実際のディレクトリ構成で確かめる。
/// </summary>
public sealed class RecordedCleanupTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), $"tnla-cleanup-{Guid.NewGuid():N}");
    private readonly string outsideDirectory = Path.Combine(Path.GetTempPath(), $"tnla-cleanup-outside-{Guid.NewGuid():N}");

    [PostgresFact]
    public async Task AFileNotRegisteredInTheDatabaseIsRemoved()
    {
        Directory.CreateDirectory(root);
        string orphan = Path.Combine(root, "orphan.ts");
        await File.WriteAllBytesAsync(orphan, [1, 2, 3]);

        await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
        PostgresRecordedRepository recorded = CreateRepository(database);

        RecordedCleanupResult result = await recorded.CleanupAsync(CancellationToken.None);

        Assert.Equal(1, result.RemovedOrphanFiles);
        Assert.False(File.Exists(orphan));
    }

    [PostgresFact]
    public async Task ARecordedVideoFileAndItsDropLogSurviveCleanup()
    {
        Directory.CreateDirectory(root);
        await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
        await RecordingTestData.SeedEpgAsync(database);

        var clock = RecordingTestData.Clock();
        var store = new TNLAStation.Infrastructure.Repositories.PostgresRecordingStore(database.ContextFactory, clock);
        (long recordedId, long videoFileId) = await store.BeginAsync(
            new TNLAStation.Application.Abstractions.RecordingStart(
                ProgramId: 1,
                RuleId: null,
                ChannelId: RecordingTestData.ChannelId,
                StartAt: RecordingTestData.Now,
                EndAt: RecordingTestData.Now.AddHours(1),
                Name: "掃除で残るべき録画",
                HalfWidthName: "掃除で残るべき録画"),
            root,
            "kept.ts",
            CancellationToken.None);
        string keptPath = Path.Combine(root, "kept.ts");
        await File.WriteAllBytesAsync(keptPath, new byte[128]);
        await store.CompleteAsync(recordedId, videoFileId, 128, CancellationToken.None);

        var defects = new TNLAStation.Domain.TransportStreamDefects(ErrorCount: 1, DropCount: 0, ScramblingCount: 0);
        await store.SaveDropLogAsync(recordedId, defects, root, "kept.drop.log", CancellationToken.None);
        string dropLogPath = Path.Combine(root, "kept.drop.log");
        await File.WriteAllTextAsync(dropLogPath, "error: 1\n");

        // 掃除の対象と無関係な、保存先の外にあるファイルも用意しておく — 触れられないことを確かめる。
        Directory.CreateDirectory(outsideDirectory);
        string outsideFile = Path.Combine(outsideDirectory, "unrelated.ts");
        await File.WriteAllBytesAsync(outsideFile, [9]);

        PostgresRecordedRepository recorded = CreateRepository(database);
        RecordedCleanupResult result = await recorded.CleanupAsync(CancellationToken.None);

        Assert.Equal(0, result.RemovedOrphanFiles);
        Assert.True(File.Exists(keptPath));
        Assert.True(File.Exists(dropLogPath));
        Assert.True(File.Exists(outsideFile));
    }

    [PostgresFact]
    public async Task EmptyDirectoriesAreRemovedButTheConfiguredRootSurvives()
    {
        Directory.CreateDirectory(root);
        string emptySubdirectory = Path.Combine(root, "empty-sub");
        Directory.CreateDirectory(emptySubdirectory);

        await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
        PostgresRecordedRepository recorded = CreateRepository(database);

        await recorded.CleanupAsync(CancellationToken.None);

        Assert.False(Directory.Exists(emptySubdirectory));
        Assert.True(Directory.Exists(root));
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }

        if (Directory.Exists(outsideDirectory))
        {
            Directory.Delete(outsideDirectory, recursive: true);
        }
    }

    private PostgresRecordedRepository CreateRepository(PostgresTestDatabase database) => new(
        database.ContextFactory,
        RecordingTestData.Clock(),
        Options.Create(new StorageOptions
        {
            RecordedDirectories = [new RecordedDirectoryOptions { Name = "recorded", Path = root }],
        }));
}
