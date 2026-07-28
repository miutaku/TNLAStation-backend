using Microsoft.EntityFrameworkCore;
using TNLAStation.Application.Abstractions;
using TNLAStation.Infrastructure.Persistence;
using TNLAStation.Infrastructure.Repositories;
using TNLAStation.Infrastructure.Transcoding;
using ApplicationEncodeTask = TNLAStation.Application.Abstractions.EncodeTask;

namespace TNLAStation.Infrastructure.Tests;

/// <summary>
/// エンコードの待ち行列。頼んだことと走ったことを別に持ち、途中で落ちた行を起動時に
/// 待ちへ戻す設計は、実データベースでないと確かめられない。
/// </summary>
public sealed class PostgresEncodeTaskListTests
{
    [PostgresFact]
    public async Task EnqueueRequiresTheSourceToBelongToTheRecording()
    {
        await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
        await RecordingTestData.SeedEpgAsync(database);
        (PostgresEncodeTaskList queue, long recordedId, long videoFileId) = await CreateAsync(database);

        long id = await queue.EnqueueAsync(Request(recordedId, videoFileId), CancellationToken.None);
        Assert.True(id > 0);

        // 別の録画のファイルを指した依頼は受けない。取り違えを黙って通すと、関係のない
        // 録画のファイルを変換してしまう。
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => queue.EnqueueAsync(Request(recordedId, videoFileId: 999), CancellationToken.None).AsTask());
    }

    [PostgresFact]
    public async Task RunningTasksAreListedAsRunning()
    {
        await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
        await RecordingTestData.SeedEpgAsync(database);
        (PostgresEncodeTaskList queue, long recordedId, long videoFileId) = await CreateAsync(database);
        long id = await queue.EnqueueAsync(Request(recordedId, videoFileId), CancellationToken.None);

        await SetStatusAsync(database, id, "running");

        ApplicationEncodeTask task = Assert.Single(await queue.ListAsync(CancellationToken.None));
        Assert.True(task.IsRunning);
    }

    [PostgresFact]
    public async Task CancelRemovesASingleTask()
    {
        await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
        await RecordingTestData.SeedEpgAsync(database);
        (PostgresEncodeTaskList queue, long recordedId, long videoFileId) = await CreateAsync(database);
        long id = await queue.EnqueueAsync(Request(recordedId, videoFileId), CancellationToken.None);

        Assert.True(await queue.CancelAsync(id, CancellationToken.None));
        Assert.Empty(await queue.ListAsync(CancellationToken.None));
        Assert.False(await queue.CancelAsync(id, CancellationToken.None));
    }

    [PostgresFact]
    public async Task CancelWaitsUntilTheRunningJobHasStopped()
    {
        await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
        await RecordingTestData.SeedEpgAsync(database);
        var jobs = new EncodeJobRegistry();
        (PostgresEncodeTaskList queue, long recordedId, long videoFileId) =
            await CreateAsync(database, jobs);
        long id = await queue.EnqueueAsync(
            Request(recordedId, videoFileId),
            CancellationToken.None);
        using var jobCancellation = new CancellationTokenSource();
        using IDisposable registration = jobs.Register(id, jobCancellation);

        Task<bool> cancel = queue.CancelAsync(id, CancellationToken.None).AsTask();
        await WaitUntilAsync(() => jobCancellation.IsCancellationRequested);

        Assert.True(jobCancellation.IsCancellationRequested);
        Assert.False(cancel.IsCompleted);

        registration.Dispose();
        Assert.True(await cancel.WaitAsync(TimeSpan.FromSeconds(2)));
    }

    [PostgresFact]
    public async Task CancelForRecordedRemovesEveryTaskOfThatRecording()
    {
        await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
        await RecordingTestData.SeedEpgAsync(database);
        (PostgresEncodeTaskList queue, long recordedId, long videoFileId) = await CreateAsync(database);
        await queue.EnqueueAsync(Request(recordedId, videoFileId), CancellationToken.None);
        await queue.EnqueueAsync(Request(recordedId, videoFileId, mode: "H.265"), CancellationToken.None);

        Assert.Equal(2, await queue.CancelForRecordedAsync(recordedId, CancellationToken.None));
        Assert.Empty(await queue.ListAsync(CancellationToken.None));
    }

    [PostgresFact]
    public async Task TheQueueViewSplitsRunningFromWaitingWithTheRecording()
    {
        await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
        await RecordingTestData.SeedEpgAsync(database);
        (PostgresEncodeTaskList queue, long recordedId, long videoFileId) = await CreateAsync(database);
        long running = await queue.EnqueueAsync(Request(recordedId, videoFileId), CancellationToken.None);
        await queue.EnqueueAsync(Request(recordedId, videoFileId, mode: "H.265"), CancellationToken.None);
        await SetStatusAsync(database, running, "running");

        Application.Models.EncodeTasks view = await ((IEncodeQueueRepository)queue).GetAsync(CancellationToken.None);

        Assert.Single(view.Running);
        Assert.Single(view.Waiting);
        Assert.Equal("録画する番組", view.Running[0].Recorded.Name);
    }

    private static async Task<(PostgresEncodeTaskList Queue, long RecordedId, long VideoFileId)> CreateAsync(
        PostgresTestDatabase database,
        EncodeJobRegistry? jobs = null)
    {
        var store = new PostgresRecordingStore(database.ContextFactory, RecordingTestData.Clock());
        var recorded = new PostgresRecordedRepository(database.ContextFactory, RecordingTestData.Clock());
        (long recordedId, long videoFileId) = await store.BeginAsync(
            new RecordingStart(
                ProgramId: 1,
                RuleId: null,
                ChannelId: RecordingTestData.ChannelId,
                StartAt: RecordingTestData.Now,
                EndAt: RecordingTestData.Now.AddHours(1),
                Name: "録画する番組",
                HalfWidthName: "録画する番組"),
            "/rec",
            "a.ts",
            CancellationToken.None);
        await store.CompleteAsync(recordedId, videoFileId, size: 4096, CancellationToken.None);

        var queue = new PostgresEncodeTaskList(
            database.ContextFactory,
            recorded,
            jobs ?? new EncodeJobRegistry(),
            RecordingTestData.Clock());
        return (queue, recordedId, videoFileId);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (int attempt = 0; attempt < 200 && !condition(); attempt++)
        {
            await Task.Delay(10);
        }
    }

    private static EncodeRequest Request(long recordedId, long videoFileId, string mode = "H.264") =>
        new(recordedId, videoFileId, mode, RemoveOriginal: false);

    private static async Task SetStatusAsync(PostgresTestDatabase database, long id, string status)
    {
        await using EpgDbContext context = await database.ContextFactory.CreateDbContextAsync();
        await context.EncodeTasks
            .Where(task => task.Id == id)
            .ExecuteUpdateAsync(task => task.SetProperty(item => item.Status, status));
    }
}
