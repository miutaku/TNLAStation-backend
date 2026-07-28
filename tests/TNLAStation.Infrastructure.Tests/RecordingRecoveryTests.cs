using Microsoft.Extensions.Logging.Abstractions;
using TNLAStation.Application.Abstractions;
using TNLAStation.Application.Models;
using TNLAStation.Domain;
using TNLAStation.Infrastructure.Recording;
using TNLAStation.Infrastructure.Repositories;

namespace TNLAStation.Infrastructure.Tests;

/// <summary>
/// 再起動したときの録画の後始末。落ちた瞬間まで録れた分は残し、1 バイトも書けていない
/// ものは無かったことにする。実際に落ちたのと同じ状態 (録画中の行 + ディスク上のファイル)
/// を作って、起動時の処理が正しく畳むことを確かめる。
/// </summary>
public sealed class RecordingRecoveryTests : IDisposable
{
    private readonly string workDirectory =
        Path.Combine(Path.GetTempPath(), $"tnla-recovery-{Guid.NewGuid():N}");

    [PostgresFact]
    public async Task ARecordingWithBytesOnDiskIsKeptAndClosed()
    {
        await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
        await RecordingTestData.SeedEpgAsync(database);
        var store = new PostgresRecordingStore(database.ContextFactory, RecordingTestData.Clock());
        var recorded = new PostgresRecordedRepository(database.ContextFactory, RecordingTestData.Clock());

        Directory.CreateDirectory(workDirectory);
        (long recordedId, _) = await store.BeginAsync(Start(1), workDirectory, "kept.ts", CancellationToken.None);
        await File.WriteAllBytesAsync(Path.Combine(workDirectory, "kept.ts"), new byte[2048]);

        await CreateRecovery(store).RecoverAsync(CancellationToken.None);

        Assert.Empty(await Recording(recorded));
        RecordedProgram program = Assert.Single((await recorded.ListAsync(new RecordedQuery(false), CancellationToken.None)).Items);
        Assert.Equal(recordedId, program.Id);
        Assert.False(program.IsRecording);
        Assert.Equal(2048, Assert.Single(program.VideoFiles!).Size);
    }

    [PostgresFact]
    public async Task AnEmptyRecordingIsDiscarded()
    {
        await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
        await RecordingTestData.SeedEpgAsync(database);
        var store = new PostgresRecordingStore(database.ContextFactory, RecordingTestData.Clock());
        var recorded = new PostgresRecordedRepository(database.ContextFactory, RecordingTestData.Clock());

        (long recordedId, _) = await store.BeginAsync(Start(1), workDirectory, "empty.ts", CancellationToken.None);

        await CreateRecovery(store).RecoverAsync(CancellationToken.None);

        Assert.Empty(await Recording(recorded));
        Assert.Empty((await recorded.ListAsync(new RecordedQuery(false), CancellationToken.None)).Items);
        Assert.False(await store.ExistsAsync(1, CancellationToken.None));
    }

    [PostgresFact]
    public async Task RecoveryRunsOnceAndLeavesFinishedRecordingsAlone()
    {
        await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
        await RecordingTestData.SeedEpgAsync(database);
        var store = new PostgresRecordingStore(database.ContextFactory, RecordingTestData.Clock());
        var recorded = new PostgresRecordedRepository(database.ContextFactory, RecordingTestData.Clock());
        Directory.CreateDirectory(workDirectory);
        (long recordedId, _) = await store.BeginAsync(Start(1), workDirectory, "kept.ts", CancellationToken.None);
        await File.WriteAllBytesAsync(Path.Combine(workDirectory, "kept.ts"), new byte[1024]);

        RecordingRecovery recovery = CreateRecovery(store);
        await recovery.RecoverAsync(CancellationToken.None);
        await recovery.RecoverAsync(CancellationToken.None);

        RecordedProgram program = Assert.Single((await recorded.ListAsync(new RecordedQuery(false), CancellationToken.None)).Items);
        Assert.Equal(recordedId, program.Id);
        Assert.Equal(1024, Assert.Single(program.VideoFiles!).Size);
    }

    public void Dispose()
    {
        if (Directory.Exists(workDirectory))
        {
            Directory.Delete(workDirectory, recursive: true);
        }
    }

    private static RecordingRecovery CreateRecovery(PostgresRecordingStore store) =>
        new(store, NullLogger<RecordingRecovery>.Instance);

    private static async Task<IReadOnlyList<RecordedProgram>> Recording(PostgresRecordedRepository recorded) =>
        (await ((IRecordingRepository)recorded).ListAsync(new RecordedQuery(false), CancellationToken.None)).Items;

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
