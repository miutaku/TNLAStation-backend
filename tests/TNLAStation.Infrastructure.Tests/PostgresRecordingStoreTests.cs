using TNLAStation.Application.Abstractions;
using TNLAStation.Application.Models;
using TNLAStation.Domain;
using TNLAStation.Infrastructure.Repositories;

namespace TNLAStation.Infrastructure.Tests;

/// <summary>
/// 録画そのものの永続化。書き始めに行を作り、途中で落ちたら畳み、取りこぼしを残す、という
/// 流れは、再起動をまたぐので実データベースでないと確かめられない。
/// </summary>
public sealed class PostgresRecordingStoreTests
{
    [PostgresFact]
    public async Task BeginCreatesARecordingInProgressWithItsFile()
    {
        await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
        await RecordingTestData.SeedEpgAsync(database);
        PostgresRecordingStore store = Create(database);
        var recorded = new PostgresRecordedRepository(database.ContextFactory, RecordingTestData.Clock());

        (long recordedId, _) = await store.BeginAsync(
            Start(programId: 1),
            "/rec",
            "a.ts",
            CancellationToken.None);

        Assert.Empty((await recorded.ListAsync(new RecordedQuery(false), CancellationToken.None)).Items);
        Page<RecordedProgram> recording = await ((IRecordingRepository)recorded)
            .ListAsync(new RecordedQuery(false), CancellationToken.None);
        RecordedProgram inProgress = Assert.Single(recording.Items);
        Assert.Equal(recordedId, inProgress.Id);
        Assert.True(inProgress.IsRecording);
    }

    [PostgresFact]
    public async Task CompleteMovesTheRecordingOutOfProgressAndRecordsTheSize()
    {
        await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
        await RecordingTestData.SeedEpgAsync(database);
        PostgresRecordingStore store = Create(database);
        var recorded = new PostgresRecordedRepository(database.ContextFactory, RecordingTestData.Clock());
        (long recordedId, long videoFileId) = await store.BeginAsync(Start(1), "/rec", "a.ts", CancellationToken.None);

        await store.CompleteAsync(recordedId, videoFileId, size: 4096, CancellationToken.None);

        Page<RecordedProgram> done = await recorded.ListAsync(new RecordedQuery(false), CancellationToken.None);
        RecordedProgram finished = Assert.Single(done.Items);
        Assert.False(finished.IsRecording);
        VideoFile file = Assert.Single(finished.VideoFiles!);
        Assert.Equal(4096, file.Size);
        Assert.Equal("ts", file.Type);
    }

    [PostgresFact]
    public async Task AbortRemovesTheRecordingEntirely()
    {
        await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
        await RecordingTestData.SeedEpgAsync(database);
        PostgresRecordingStore store = Create(database);
        var recorded = new PostgresRecordedRepository(database.ContextFactory, RecordingTestData.Clock());
        (long recordedId, _) = await store.BeginAsync(Start(1), "/rec", "a.ts", CancellationToken.None);

        await store.AbortAsync(recordedId, CancellationToken.None);

        Assert.Empty((await recorded.ListAsync(new RecordedQuery(false), CancellationToken.None)).Items);
        Page<RecordedProgram> recording = await ((IRecordingRepository)recorded)
            .ListAsync(new RecordedQuery(false), CancellationToken.None);
        Assert.Empty(recording.Items);
        Assert.False(await store.ExistsAsync(1, CancellationToken.None));
    }

    [PostgresFact]
    public async Task ExistsFindsARecordingOfTheSameProgram()
    {
        await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
        await RecordingTestData.SeedEpgAsync(database);
        PostgresRecordingStore store = Create(database);
        await store.BeginAsync(Start(programId: 42), "/rec", "a.ts", CancellationToken.None);

        Assert.True(await store.ExistsAsync(42, CancellationToken.None));
        Assert.False(await store.ExistsAsync(43, CancellationToken.None));
    }

    [PostgresFact]
    public async Task UnfinishedRecordingsAreListedForRecovery()
    {
        await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
        await RecordingTestData.SeedEpgAsync(database);
        PostgresRecordingStore store = Create(database);
        (long recordedId, long videoFileId) = await store.BeginAsync(Start(1), "/rec", "a.ts", CancellationToken.None);
        await store.BeginAsync(Start(2), "/rec", "b.ts", CancellationToken.None);
        await store.CompleteAsync(recordedId, videoFileId, 1, CancellationToken.None);

        IReadOnlyList<UnfinishedRecording> unfinished = await store.ListUnfinishedAsync(CancellationToken.None);
        UnfinishedRecording pending = Assert.Single(unfinished);
        Assert.Equal("b.ts", pending.Filename);
        Assert.Equal("/rec", pending.ParentDirectoryName);
    }

    [PostgresFact]
    public async Task TheDropLogIsSavedAndReadBack()
    {
        await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
        await RecordingTestData.SeedEpgAsync(database);
        PostgresRecordingStore store = Create(database);
        var recorded = new PostgresRecordedRepository(database.ContextFactory, RecordingTestData.Clock());
        (long recordedId, long videoFileId) = await store.BeginAsync(Start(1), "/rec", "a.ts", CancellationToken.None);
        await store.CompleteAsync(recordedId, videoFileId, 1, CancellationToken.None);

        await store.SaveDropLogAsync(
            recordedId,
            new TransportStreamDefects(ErrorCount: 2, DropCount: 5, ScramblingCount: 1),
            "/rec",
            "a.drop.log",
            CancellationToken.None);

        RecordedProgram? program = await recorded.GetAsync(recordedId, CancellationToken.None);
        Assert.Equal(5, program!.DropLogFile!.DropCount);
        DropLogFileLocation? location = await ((IDropLogRepository)store)
            .GetAsync(program.DropLogFile!.Id, CancellationToken.None);
        Assert.Equal("/rec/a.drop.log", location!.FullPath);
    }

    [PostgresFact]
    public async Task SavingTheDropLogTwiceKeepsTheFirst()
    {
        await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
        await RecordingTestData.SeedEpgAsync(database);
        PostgresRecordingStore store = Create(database);
        var recorded = new PostgresRecordedRepository(database.ContextFactory, RecordingTestData.Clock());
        (long recordedId, long videoFileId) = await store.BeginAsync(Start(1), "/rec", "a.ts", CancellationToken.None);
        await store.CompleteAsync(recordedId, videoFileId, 1, CancellationToken.None);

        await store.SaveDropLogAsync(recordedId, new TransportStreamDefects(0, 3, 0), "/rec", "a.drop.log", CancellationToken.None);
        await store.SaveDropLogAsync(recordedId, new TransportStreamDefects(9, 9, 9), "/rec", "a.drop.log", CancellationToken.None);

        RecordedProgram? program = await recorded.GetAsync(recordedId, CancellationToken.None);
        Assert.Equal(3, program!.DropLogFile!.DropCount);
    }

    private static PostgresRecordingStore Create(PostgresTestDatabase database) =>
        new(database.ContextFactory, RecordingTestData.Clock());

    private static RecordingStart Start(long programId) =>
        new(
            ProgramId: programId,
            RuleId: null,
            ChannelId: RecordingTestData.ChannelId,
            StartAt: RecordingTestData.Now,
            EndAt: RecordingTestData.Now.AddHours(1),
            Name: "録画する番組",
            HalfWidthName: "録画する番組");
}
