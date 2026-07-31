using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TNLAStation.Application.Abstractions;
using TNLAStation.Application.Models;
using TNLAStation.Domain;
using TNLAStation.Infrastructure.CommandHooks;
using TNLAStation.Infrastructure.Configuration;
using TNLAStation.Infrastructure.Mirakurun;
using TNLAStation.Infrastructure.Persistence;
using TNLAStation.Infrastructure.Recording;
using TNLAStation.Infrastructure.Repositories;
using TNLAStation.Infrastructure.Reserves;

namespace TNLAStation.Infrastructure.Tests;

/// <summary>
/// 録画停止は、予約行だけ・録画行だけの試験では競合を見落とす。実 DB と、停止されるまで
/// データを返し続ける受信ストリームを一緒に動かして、応答時点の状態まで確かめる。
/// </summary>
public sealed class RecordingStopTests
{
    [PostgresFact]
    public async Task AddingTheCurrentProgramWakesTheSchedulerAndStartsRecordingImmediately()
    {
        await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
        EpgProgram currentProgram = RecordingTestData.CreateProgram(
            startAt: RecordingTestData.Now.AddMinutes(-10));
        await RecordingTestData.SeedEpgAsync(database, currentProgram);
        string directory = Path.Combine(Path.GetTempPath(), $"tnla-immediate-recording-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);

        PostgresReserveRepository reserves = CreateReserves(database);
        var schedulerReserves = new CountingReserveRepository(reserves);
        var clock = RecordingTestData.Clock();
        var scheduleSignal = new RecordingScheduleSignal();
        var stream = new BlockingTransportStream();
        var mirakurun = new SingleStreamMirakurun(stream);
        var store = new PostgresRecordingStore(database.ContextFactory, clock);
        using var generator = new ReserveGenerator(
            new PostgresEpgRepository(database.ContextFactory, Options.Create(new EpgOptions()), clock),
            new PostgresRuleRepository(database.ContextFactory),
            new PostgresRecordedHistoryStore(database.ContextFactory),
            reserves,
            mirakurun,
            new CommandHookRunner(NullLogger<CommandHookRunner>.Instance),
            Options.Create(new ReserveOptions()),
            Options.Create(new CommandHookOptions()),
            scheduleSignal,
            NullClientNotifier.Instance,
            clock,
            NullLogger<ReserveGenerator>.Instance);
        using var scheduler = new RecordingScheduler(
            schedulerReserves,
            store,
            Unused.Instance,
            new PostgresEpgRepository(database.ContextFactory, Options.Create(new EpgOptions()), clock),
            mirakurun,
            new NoThumbnailService(),
            new PostgresRecordedHistoryStore(database.ContextFactory),
            new CommandHookRunner(NullLogger<CommandHookRunner>.Instance),
            NullClientNotifier.Instance,
            new RecordingRecovery(store, NullLogger<RecordingRecovery>.Instance),
            Options.Create(new RecordingOptions
            {
                Directory = directory,
                // 通知が無ければ試験時間内には再確認されない値にし、ポーリングによる偶然を除く。
                PollIntervalSeconds = 3600,
            }),
            Options.Create(new StorageOptions()),
            Options.Create(new MirakurunOptions()),
            Options.Create(new CommandHookOptions()),
            new ImmediateLeaseProvider(),
            new RecordingJobRegistry(),
            scheduleSignal,
            clock,
            NullLogger<RecordingScheduler>.Instance);

        try
        {
            await scheduler.StartAsync(CancellationToken.None);
            await schedulerReserves.WaitForFirstListAsync(TimeSpan.FromSeconds(10));

            // 予約作成 endpoint と同じ順序: 手動予約を保存してから予約表の再生成を依頼する。
            await reserves.AddAsync(ManualOnProgram(currentProgram.Id), CancellationToken.None);
            await generator.RequestAsync(CancellationToken.None);

            await stream.WaitUntilBlockedAsync(TimeSpan.FromSeconds(10));

            Reservation generated = Assert.Single(
                (await reserves.ListAsync(new ReserveQuery(false, Type: "normal"), CancellationToken.None)).Items);
            Assert.Equal(currentProgram.Id, generated.ProgramId);
            Assert.True(generated.StartAt < clock.GetUtcNow().ToUnixTimeMilliseconds());
            Assert.True(generated.EndAt > clock.GetUtcNow().ToUnixTimeMilliseconds());

            var recordings = (IRecordingRepository)new PostgresRecordedRepository(
                database.ContextFactory,
                clock);
            RecordedProgram recording = Assert.Single(
                (await recordings.ListAsync(new RecordedQuery(false), CancellationToken.None)).Items);
            Assert.True(recording.IsRecording);
            Assert.Equal(currentProgram.Id, recording.ProgramId);

            // 予約表は生成のたびに行 ID が入れ替わる。別の予約追加で再生成しても、安定キーが
            // 同じ録画を別物と判断して止めてはいけない。
            int listsBeforeRegeneration = schedulerReserves.ListCount;
            await reserves.AddAsync(
                new CreateReserveCommand(
                    AllowEndLack: true,
                    ProgramId: null,
                    TimeSpecified: new TimeSpecifiedReserve(
                        "後続番組",
                        RecordingTestData.ChannelId,
                        RecordingTestData.Now.AddHours(2).ToUnixTimeMilliseconds(),
                        RecordingTestData.Now.AddHours(3).ToUnixTimeMilliseconds()),
                    Tags: null,
                    Save: null,
                    Encode: null),
                CancellationToken.None);
            await generator.RequestAsync(CancellationToken.None);
            await schedulerReserves.WaitForListCountAsync(listsBeforeRegeneration + 1, TimeSpan.FromSeconds(10));
            // もう一度通知して次の tick が始まるまで待てば、再生成を見た最初の tick は確実に
            // StopFinishedAsync まで完了している。
            await scheduleSignal.RequestAsync(CancellationToken.None);
            await schedulerReserves.WaitForListCountAsync(listsBeforeRegeneration + 2, TimeSpan.FromSeconds(10));

            Assert.False(stream.IsDisposed);
            recording = Assert.Single(
                (await recordings.ListAsync(new RecordedQuery(false), CancellationToken.None)).Items);
            Assert.True(recording.IsRecording);
        }
        finally
        {
            await scheduler.StopAsync(CancellationToken.None);
            Directory.Delete(directory, recursive: true);
        }
    }

    [PostgresFact]
    public async Task StopWaitsForTheStreamAndKeepsThePartialFileWhileDeletingTheManualReserve()
    {
        await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
        await RecordingTestData.SeedEpgAsync(
            database,
            RecordingTestData.CreateProgram(startAt: RecordingTestData.Now.AddMinutes(-1)));
        string directory = Path.Combine(Path.GetTempPath(), $"tnla-recording-stop-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);

        PostgresReserveRepository reserves = CreateReserves(database);
        await reserves.AddAsync(ManualOnProgram(programId: 1), CancellationToken.None);
        await PublishManualReservesAsync(reserves);

        var clock = RecordingTestData.Clock();
        var store = new PostgresRecordingStore(database.ContextFactory, clock);
        var jobs = new RecordingJobRegistry();
        var stream = new BlockingTransportStream();
        using var scheduler = new RecordingScheduler(
            reserves,
            store,
            Unused.Instance,
            new PostgresEpgRepository(
                database.ContextFactory,
                Options.Create(new EpgOptions()),
                clock),
            new SingleStreamMirakurun(stream),
            new NoThumbnailService(),
            new PostgresRecordedHistoryStore(database.ContextFactory),
            new CommandHookRunner(NullLogger<CommandHookRunner>.Instance),
            NullClientNotifier.Instance,
            new RecordingRecovery(store, NullLogger<RecordingRecovery>.Instance),
            Options.Create(new RecordingOptions { Directory = directory, PollIntervalSeconds = 1 }),
            Options.Create(new StorageOptions()),
            Options.Create(new MirakurunOptions()),
            Options.Create(new CommandHookOptions()),
            new ImmediateLeaseProvider(),
            jobs,
            new RecordingScheduleSignal(),
            clock,
            NullLogger<RecordingScheduler>.Instance);
        var stop = new PostgresRecordingStopService(database.ContextFactory, reserves, jobs);

        try
        {
            await scheduler.StartAsync(CancellationToken.None);
            await stream.WaitUntilBlockedAsync(TimeSpan.FromSeconds(10));

            var recordingRepository = (IRecordingRepository)new PostgresRecordedRepository(
                database.ContextFactory,
                clock);
            Page<RecordedProgram> recording = await recordingRepository.ListAsync(
                new RecordedQuery(false),
                CancellationToken.None);
            RecordedProgram current = Assert.Single(recording.Items);
            VideoFile original = Assert.Single(current.VideoFiles!);
            string path = Path.Combine(directory, original.Filename);

            var elapsed = Stopwatch.StartNew();
            Assert.True(await stop.StopAsync(current.Id, CancellationToken.None));
            elapsed.Stop();

            Assert.True(stream.IsDisposed);
            Assert.True(File.Exists(path));
            Assert.Equal(BlockingTransportStream.PayloadLength, new FileInfo(path).Length);
            Assert.Empty(await reserves.ListManualReservesAsync(CancellationToken.None));
            Assert.Empty((await reserves.ListAsync(new ReserveQuery(false), CancellationToken.None)).Items);

            var recordedRepository = new PostgresRecordedRepository(database.ContextFactory, clock);
            RecordedProgram completed = Assert.Single(
                (await recordedRepository.ListAsync(new RecordedQuery(false), CancellationToken.None)).Items);
            Assert.False(completed.IsRecording);
            Assert.Equal(BlockingTransportStream.PayloadLength, Assert.Single(completed.VideoFiles!).Size);
            Assert.Empty((await recordingRepository.ListAsync(
                new RecordedQuery(false),
                CancellationToken.None)).Items);

            await using EpgDbContext context = await database.ContextFactory.CreateDbContextAsync();
            var identity = await context.Recorded.AsNoTracking()
                .Where(item => item.Id == current.Id)
                .Select(item => new { item.ReserveId, item.ReserveKey, item.ManualReserveId })
                .SingleAsync();
            Assert.NotNull(identity.ReserveId);
            Assert.StartsWith("manual:", identity.ReserveKey, StringComparison.Ordinal);
            Assert.NotNull(identity.ManualReserveId);

            // StopAsync は registry の完了、つまりストリーム破棄と DB 確定より前には返らない。
            Assert.True(elapsed.Elapsed < TimeSpan.FromSeconds(10));
        }
        finally
        {
            await scheduler.StopAsync(CancellationToken.None);
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// 予約で選んだエンコードは、録画完了時に待ち行列へ入らなければ誰も走らせない。
    /// 予約の設定が待ち行列の 1 行になるところまでを実 DB で確かめる。
    /// </summary>
    [PostgresFact]
    public async Task FinishingARecordingQueuesTheEncodeChosenOnTheReserve()
    {
        await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
        await RecordingTestData.SeedEpgAsync(
            database,
            RecordingTestData.CreateProgram(startAt: RecordingTestData.Now.AddMinutes(-1)));
        string directory = Path.Combine(Path.GetTempPath(), $"tnla-recording-encode-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);

        PostgresReserveRepository reserves = CreateReserves(database);
        await reserves.AddAsync(
            ManualOnProgram(programId: 1) with
            {
                Encode = new ReserveEncodeSettings(
                    Mode1: "H.265",
                    EncodeParentDirectoryName1: null,
                    Directory1: null,
                    Mode2: null,
                    EncodeParentDirectoryName2: null,
                    Directory2: null,
                    Mode3: null,
                    EncodeParentDirectoryName3: null,
                    Directory3: null,
                    IsDeleteOriginalAfterEncode: true),
            },
            CancellationToken.None);
        await PublishManualReservesAsync(reserves);

        var clock = RecordingTestData.Clock();
        var store = new PostgresRecordingStore(database.ContextFactory, clock);
        var recordedRepository = new PostgresRecordedRepository(database.ContextFactory, clock);
        var encodeTasks = new PostgresEncodeTaskList(database.ContextFactory, recordedRepository, clock);
        var jobs = new RecordingJobRegistry();
        var stream = new BlockingTransportStream();
        using var scheduler = new RecordingScheduler(
            reserves,
            store,
            encodeTasks,
            new PostgresEpgRepository(database.ContextFactory, Options.Create(new EpgOptions()), clock),
            new SingleStreamMirakurun(stream),
            new NoThumbnailService(),
            new PostgresRecordedHistoryStore(database.ContextFactory),
            new CommandHookRunner(NullLogger<CommandHookRunner>.Instance),
            NullClientNotifier.Instance,
            new RecordingRecovery(store, NullLogger<RecordingRecovery>.Instance),
            Options.Create(new RecordingOptions { Directory = directory, PollIntervalSeconds = 1 }),
            Options.Create(new StorageOptions()),
            Options.Create(new MirakurunOptions()),
            Options.Create(new CommandHookOptions()),
            new ImmediateLeaseProvider(),
            jobs,
            new RecordingScheduleSignal(),
            clock,
            NullLogger<RecordingScheduler>.Instance);
        var stop = new PostgresRecordingStopService(database.ContextFactory, reserves, jobs);

        try
        {
            await scheduler.StartAsync(CancellationToken.None);
            await stream.WaitUntilBlockedAsync(TimeSpan.FromSeconds(10));

            var recordings = (IRecordingRepository)recordedRepository;
            RecordedProgram current = Assert.Single(
                (await recordings.ListAsync(new RecordedQuery(false), CancellationToken.None)).Items);
            VideoFile original = Assert.Single(current.VideoFiles!);

            Assert.True(await stop.StopAsync(current.Id, CancellationToken.None));

            EncodeTask queued = Assert.Single(await encodeTasks.ListAsync(CancellationToken.None));
            Assert.Equal(current.Id, queued.RecordedId);
            Assert.Equal(original.Id, queued.SourceVideoFileId);
            Assert.Equal("H.265", queued.Mode);
            Assert.True(queued.RemoveOriginal);
            Assert.False(queued.IsRunning);
            // 出力先の指定が無い予約は、上流の既定と同じく元ファイルの隣へ出す。
            Assert.Null(queued.ParentDirectoryName);
            Assert.Null(queued.Directory);
        }
        finally
        {
            await scheduler.StopAsync(CancellationToken.None);
            Directory.Delete(directory, recursive: true);
        }
    }

    [PostgresFact]
    public async Task StoppingARuleRecordingLeavesASkipThatSurvivesReserveRegeneration()
    {
        await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
        await RecordingTestData.SeedEpgAsync(database, RecordingTestData.CreateProgram());
        PostgresReserveRepository reserves = CreateReserves(database);
        await reserves.ReplaceAsync(
            [RuleAssignment(ruleId: 5, programId: 1)],
            RecordingTestData.Now,
            CancellationToken.None);
        Reservation reserve = Assert.Single(
            (await reserves.ListAsync(new ReserveQuery(false), CancellationToken.None)).Items);

        var clock = RecordingTestData.Clock();
        var store = new PostgresRecordingStore(database.ContextFactory, clock);
        (long recordedId, long videoFileId) = await store.BeginAsync(
            new RecordingStart(
                ProgramId: 1,
                RuleId: 5,
                ChannelId: RecordingTestData.ChannelId,
                StartAt: RecordingTestData.Now,
                EndAt: RecordingTestData.Now.AddHours(1),
                Name: "ルール録画",
                HalfWidthName: "ルール録画",
                ReserveId: reserve.Id,
                ReserveKey: reserve.ReserveKey),
            "/recorded",
            "rule.ts",
            CancellationToken.None);

        // 録画中にも予約一覧は再生成され、予約 ID は変わる。registry と録画行が持つ古い ID
        // だけに頼ると停止できないため、同じ安定キーの新しい予約を引き直せることを確かめる。
        await reserves.ReplaceAsync(
            [RuleAssignment(ruleId: 5, programId: 1)],
            RecordingTestData.Now.AddMinutes(1),
            CancellationToken.None);
        Reservation replacement = Assert.Single(
            (await reserves.ListAsync(new ReserveQuery(false), CancellationToken.None)).Items);
        Assert.NotEqual(reserve.Id, replacement.Id);

        var jobs = new RecordingJobRegistry();
        using var cancellation = new CancellationTokenSource();
        IDisposable registration = jobs.Register(
            new RecordingJobIdentity(recordedId, reserve.Id, reserve.ReserveKey!, null),
            cancellation);
        var cancellationObserved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFinalization = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task finalizer = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellation.Token);
            }
            catch (OperationCanceledException)
            {
                cancellationObserved.TrySetResult();
            }

            await releaseFinalization.Task;
            await store.CompleteAsync(recordedId, videoFileId, size: 188, CancellationToken.None);
            registration.Dispose();
        });
        var stop = new PostgresRecordingStopService(database.ContextFactory, reserves, jobs);

        Task<bool> stopping = stop.StopAsync(recordedId, CancellationToken.None).AsTask();
        await cancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(stopping.IsCompleted);
        releaseFinalization.TrySetResult();
        Assert.True(await stopping.WaitAsync(TimeSpan.FromSeconds(5)));
        await finalizer;

        ReserveStates states = await reserves.ListStatesAsync(CancellationToken.None);
        Assert.Contains("rule:5:1", states.Skipped);
        await reserves.ReplaceAsync(
            [RuleAssignment(ruleId: 5, programId: 1, isSkip: states.Skipped.Contains("rule:5:1"))],
            RecordingTestData.Now,
            CancellationToken.None);
        Assert.True(Assert.Single(
            (await reserves.ListAsync(new ReserveQuery(false), CancellationToken.None)).Items).IsSkip);
    }

    private static PostgresReserveRepository CreateReserves(PostgresTestDatabase database) =>
        new(database.ContextFactory, RecordingTestData.Clock());

    private static CreateReserveCommand ManualOnProgram(long programId) =>
        new(
            AllowEndLack: true,
            ProgramId: programId,
            TimeSpecified: null,
            Tags: null,
            Save: null,
            Encode: null);

    private static async Task PublishManualReservesAsync(PostgresReserveRepository repository)
    {
        IReadOnlyList<ManualReserve> manuals = await repository.ListManualReservesAsync(CancellationToken.None);
        await repository.ReplaceAsync(
            [.. manuals.Select(manual => new ReserveAssignment(
                new ReserveTarget(
                    ReserveSource.Manual,
                    manual.ChannelId,
                    manual.ChannelType,
                    manual.StartAt,
                    manual.EndAt,
                    manual.Name,
                    manual.ProgramId,
                    ManualReserveId: manual.Id),
                TunerIndex: 0))],
            RecordingTestData.Now,
            CancellationToken.None);
    }

    private static ReserveAssignment RuleAssignment(long ruleId, long programId, bool isSkip = false) =>
        new(
            new ReserveTarget(
                ReserveSource.Rule,
                RecordingTestData.ChannelId,
                "GR",
                RecordingTestData.Now,
                RecordingTestData.Now.AddHours(1),
                "ルール録画",
                programId,
                RuleId: ruleId,
                IsSkip: isSkip),
            TunerIndex: isSkip ? null : 0);

    private sealed class ImmediateLeaseProvider : IRecordingLeaseProvider
    {
        public ValueTask<IAsyncDisposable?> TryAcquireAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult<IAsyncDisposable?>(new Lease());

        private sealed class Lease : IAsyncDisposable
        {
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    private sealed class SingleStreamMirakurun(BlockingTransportStream stream) : IMirakurunClient
    {
        public ValueTask<Stream> OpenServiceStreamAsync(long channelId, CancellationToken cancellationToken, int? priority = null) =>
            ValueTask.FromResult<Stream>(stream);

        public ValueTask<IReadOnlyList<MirakurunServiceDto>> GetServicesAsync(
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public ValueTask<IReadOnlyList<MirakurunProgramDto>> GetProgramsAsync(
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<MirakurunEventDto> ReadEventsAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public ValueTask<IReadOnlyList<MirakurunTunerDto>> GetTunersAsync(
            CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<MirakurunTunerDto>>(
            [
                new()
                {
                    Index = 0,
                    Name = "GR tuner",
                    Types = ["GR"],
                    IsAvailable = true,
                    IsFault = false,
                },
            ]);
    }

    private sealed class CountingReserveRepository(IReserveRepository inner) : IReserveRepository
    {
        private readonly TaskCompletionSource firstList =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int listCount;

        public int ListCount => Volatile.Read(ref listCount);

        public async ValueTask<Page<Reservation>> ListAsync(
            ReserveQuery query,
            CancellationToken cancellationToken)
        {
            Page<Reservation> page = await inner.ListAsync(query, cancellationToken);
            Interlocked.Increment(ref listCount);
            firstList.TrySetResult();
            return page;
        }

        public Task WaitForFirstListAsync(TimeSpan timeout) => firstList.Task.WaitAsync(timeout);

        public async Task WaitForListCountAsync(int expected, TimeSpan timeout)
        {
            using var cancellation = new CancellationTokenSource(timeout);
            while (ListCount < expected)
            {
                await Task.Delay(10, cancellation.Token);
            }
        }

        public ValueTask<long> AddAsync(CreateReserveCommand command, CancellationToken cancellationToken) =>
            inner.AddAsync(command, cancellationToken);

        public ValueTask<Reservation?> GetAsync(long reserveId, CancellationToken cancellationToken) =>
            inner.GetAsync(reserveId, cancellationToken);

        public ValueTask<bool> DeleteAsync(long reserveId, CancellationToken cancellationToken) =>
            inner.DeleteAsync(reserveId, cancellationToken);

        public ValueTask<bool> SetSkipAsync(
            long reserveId,
            bool isSkip,
            CancellationToken cancellationToken) =>
            inner.SetSkipAsync(reserveId, isSkip, cancellationToken);

        public ValueTask<bool> ClearOverlapAsync(long reserveId, CancellationToken cancellationToken) =>
            inner.ClearOverlapAsync(reserveId, cancellationToken);

        public ValueTask<bool> UpdateAsync(
            long reserveId,
            CreateReserveCommand command,
            CancellationToken cancellationToken) =>
            inner.UpdateAsync(reserveId, command, cancellationToken);
    }

    private sealed class NoThumbnailService : IThumbnailService
    {
        public ValueTask<ThumbnailFile?> GetAsync(long thumbnailId, CancellationToken cancellationToken) =>
            ValueTask.FromResult<ThumbnailFile?>(null);

        public ValueTask<long?> CreateForVideoFileAsync(long videoFileId, CancellationToken cancellationToken) =>
            ValueTask.FromResult<long?>(null);

        public ValueTask<int> CreateMissingAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult(0);

        public ValueTask<bool> DeleteAsync(long thumbnailId, CancellationToken cancellationToken) =>
            ValueTask.FromResult(false);

        public ValueTask<int> CleanupAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult(0);
    }

    private sealed class BlockingTransportStream : Stream
    {
        private static readonly byte[] Payload = CreatePayload();
        private readonly TaskCompletionSource blocked =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int reads;

        public static int PayloadLength => Payload.Length;

        public bool IsDisposed { get; private set; }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public Task WaitUntilBlockedAsync(TimeSpan timeout) => blocked.Task.WaitAsync(timeout);

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref reads) == 1)
            {
                Payload.AsMemory().CopyTo(buffer);
                return ValueTask.FromResult(Payload.Length);
            }

            blocked.TrySetResult();
            return WaitForCancellationAsync(cancellationToken);
        }

        public override ValueTask DisposeAsync()
        {
            IsDisposed = true;
            return base.DisposeAsync();
        }

        public override void Flush() => throw new NotSupportedException();

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        private static async ValueTask<int> WaitForCancellationAsync(CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return 0;
        }

        private static byte[] CreatePayload()
        {
            var payload = new byte[188 * 4];
            for (int offset = 0; offset < payload.Length; offset += 188)
            {
                payload[offset] = 0x47;
            }

            return payload;
        }
    }
}
