using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TNLAStation.Application.Abstractions;
using TNLAStation.Infrastructure.Configuration;
using TNLAStation.Infrastructure.Recording;
using TNLAStation.Infrastructure.Repositories;
using TNLAStation.Infrastructure.Transcoding;

namespace TNLAStation.Infrastructure.Tests;

/// <summary>
/// 実際に ffmpeg を回す部分は ffmpeg-worker (TNLAStation.FfmpegWorker.Tests の
/// EncodeRunnerCancellationTests) が確かめる。ここでは EncodeWorker が持つ待ち行列・DB
/// 更新側の取り消しだけを、実行を模した fake executor で確かめる。
/// </summary>
public sealed class EncodeWorkerCancellationTests
{
    [PostgresFact]
    public async Task CancelRemovesTheQueuedTaskAndLeavesNoEncodedArtifact()
    {
        await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
        await RecordingTestData.SeedEpgAsync(database);
        string directory = Path.Combine(Path.GetTempPath(), $"tnla-encode-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string sourcePath = Path.Combine(directory, "input.ts");
        await File.WriteAllBytesAsync(sourcePath, [0x47, 0x00, 0x00, 0x10]);

        var clock = RecordingTestData.Clock();
        var store = new PostgresRecordingStore(database.ContextFactory, clock);
        (long recordedId, long videoFileId) = await store.BeginAsync(
            new RecordingStart(
                ProgramId: 1,
                RuleId: null,
                ChannelId: RecordingTestData.ChannelId,
                StartAt: RecordingTestData.Now,
                EndAt: RecordingTestData.Now.AddHours(1),
                Name: "キャンセル試験",
                HalfWidthName: "キャンセル試験"),
            directory,
            Path.GetFileName(sourcePath),
            CancellationToken.None);
        await store.CompleteAsync(
            recordedId,
            videoFileId,
            new FileInfo(sourcePath).Length,
            CancellationToken.None);

        var jobs = new EncodeJobRegistry();
        var recorded = new PostgresRecordedRepository(database.ContextFactory, clock);
        var queue = new PostgresEncodeTaskList(database.ContextFactory, recorded, jobs, clock);
        long encodeId = await queue.EnqueueAsync(
            new EncodeRequest(recordedId, videoFileId, "H.264", RemoveOriginal: false),
            CancellationToken.None);
        var videoFiles = new PostgresVideoFileRepository(
            database.ContextFactory,
            Options.Create(new StorageOptions()),
            clock);
        var executor = new BlockingEncodeExecutor();
        var epg = new PostgresEpgRepository(database.ContextFactory, Options.Create(new EpgOptions()), clock);
        using var worker = new EncodeWorker(
            database.ContextFactory,
            videoFiles,
            recorded,
            epg,
            store,
            executor,
            new TNLAStation.Infrastructure.CommandHooks.CommandHookRunner(NullLogger<TNLAStation.Infrastructure.CommandHooks.CommandHookRunner>.Instance),
            Options.Create(new EncodeOptions { PollIntervalSeconds = 1 }),
            Options.Create(new CommandHookOptions()),
            new ImmediateLeaseProvider(),
            jobs,
            NullClientNotifier.Instance,
            clock,
            NullLogger<EncodeWorker>.Instance);

        try
        {
            await worker.StartAsync(CancellationToken.None);
            await executor.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));

            Assert.True(await queue.CancelAsync(encodeId, CancellationToken.None));
            await executor.Canceled.Task.WaitAsync(TimeSpan.FromSeconds(10));

            // Canceled が立った時点では、EncodeWorker 側の後始末 (partial 削除・行の削除) が
            // まだ終わっていないことがある。行が消えるまで待てば、その後始末も完了している。
            await WaitUntilTaskRemovedAsync(database, encodeId, TimeSpan.FromSeconds(10));

            await using var context = await database.ContextFactory.CreateDbContextAsync();
            Assert.False(await context.EncodeTasks.AnyAsync(task => task.Id == encodeId));
            Assert.Equal(
                1,
                await context.VideoFiles.CountAsync(file => file.RecordedId == recordedId));
            Assert.False(File.Exists(executor.LastPartialPath));
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
            Directory.Delete(directory, recursive: true);
        }
    }

    private static async Task WaitUntilTaskRemovedAsync(PostgresTestDatabase database, long encodeId, TimeSpan timeout)
    {
        long startedAt = System.Diagnostics.Stopwatch.GetTimestamp();
        while (true)
        {
            await using var context = await database.ContextFactory.CreateDbContextAsync();
            if (!await context.EncodeTasks.AnyAsync(task => task.Id == encodeId))
            {
                return;
            }

            if (System.Diagnostics.Stopwatch.GetElapsedTime(startedAt) >= timeout)
            {
                throw new TimeoutException("The encode task row was not removed in time.");
            }

            await Task.Delay(20);
        }
    }

    private sealed class ImmediateLeaseProvider : IRecordingLeaseProvider
    {
        public ValueTask<IAsyncDisposable?> TryAcquireAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult<IAsyncDisposable?>(new Lease());

        private sealed class Lease : IAsyncDisposable
        {
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    /// <summary>
    /// 実行が始まったこと・取り消されたことを外から観測できる fake。partial ファイルを
    /// 実際に書き出してから止まるので、EncodeWorker 側の後始末 (TryDelete) も確かめられる。
    /// </summary>
    private sealed class BlockingEncodeExecutor : IEncodeExecutor
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Canceled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public string? LastPartialPath { get; private set; }

        public async Task<bool> RunAsync(
            string inputPath,
            string outputPath,
            IReadOnlyList<string> arguments,
            string? command,
            double? rateTimeoutMultiplier,
            IReadOnlyDictionary<string, string> environmentVariables,
            Func<int?, string?, CancellationToken, Task> onProgress,
            CancellationToken cancellationToken)
        {
            LastPartialPath = outputPath;
            await File.WriteAllTextAsync(outputPath, "partial", CancellationToken.None);
            await onProgress(10, "encoding", cancellationToken);
            Started.TrySetResult();

            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return true;
            }
            catch (OperationCanceledException)
            {
                // 後始末 (partial ファイルの削除) は呼び出し側の EncodeWorker.EncodeAsync が行う。
                // ここで消してしまうと、その後始末を試験できなくなる。
                Canceled.TrySetResult();
                throw;
            }
        }
    }
}
