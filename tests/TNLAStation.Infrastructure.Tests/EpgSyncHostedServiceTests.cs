using System.Threading.Channels;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using TNLAStation.Application.Abstractions;
using TNLAStation.Domain;
using TNLAStation.Infrastructure.Configuration;
using TNLAStation.Infrastructure.Mirakurun;

namespace TNLAStation.Infrastructure.Tests;

/// <summary>
/// EPG 同期の異常系。チューナーは落ちるものなので、落ちたときに番組表を壊さず、失敗を記録して
/// 復帰することを確かめる。時計を差し替えて、待ち時間を実時間で待たずに進める。
/// </summary>
public sealed class EpgSyncHostedServiceTests
{
    private static readonly DateTimeOffset Start = new(2026, 7, 20, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task AFullSnapshotIsCommittedBeforeTheEventStreamIsApplied()
    {
        var client = new FakeMirakurunClient();
        var store = new RecordingEpgStore();
        using EpgSyncHostedService service = CreateService(client, store, out FakeTimeProvider time);

        await service.StartAsync(CancellationToken.None);
        await store.SnapshotCommitted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        // 停止そのものが失敗として記録され得るので、止める前の状態を判定する。
        IReadOnlyList<EpgSnapshot> committed = store.Snapshots;
        IReadOnlyList<string> failuresBeforeStop = store.Failures;
        await service.StopAsync(CancellationToken.None);

        EpgSnapshot snapshot = Assert.Single(committed);
        Assert.Equal("ＮＨＫ総合１・東京", Assert.Single(snapshot.Channels).Name);
        Assert.Single(snapshot.Programs);
        Assert.Equal(Start, snapshot.CapturedAt);
        Assert.Empty(failuresBeforeStop);
    }

    [Fact]
    public async Task AnEventStreamThatEndsIsRecordedAsAFailureAndTheSnapshotSurvives()
    {
        var client = new FakeMirakurunClient { EndEventStreamImmediately = true };
        var store = new RecordingEpgStore();
        using EpgSyncHostedService service = CreateService(client, store, out FakeTimeProvider time);

        await service.StartAsync(CancellationToken.None);
        await store.FailureRecorded.Task.WaitAsync(TimeSpan.FromSeconds(5));
        IReadOnlyList<EpgSnapshot> committed = store.Snapshots;
        IReadOnlyList<string> failures = store.Failures;
        await service.StopAsync(CancellationToken.None);

        // 直前に確定したスナップショットは残したまま、失敗だけを記録する。
        Assert.NotEmpty(committed);
        Assert.Contains("Mirakurun event producer completed unexpectedly.", failures);
    }

    [Fact]
    public async Task AFailingSnapshotFetchIsRecordedAndRetried()
    {
        var client = new FakeMirakurunClient { ServicesError = new HttpRequestException("tuner is offline") };
        var store = new RecordingEpgStore();
        using EpgSyncHostedService service = CreateService(client, store, out FakeTimeProvider time);

        await service.StartAsync(CancellationToken.None);
        await store.FailureRecorded.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Empty(store.Snapshots);
        Assert.Contains("tuner is offline", store.Failures);

        // 失敗後は待ってから再試行する。待ちに入るのを待ちつつ時計を進める。
        int attemptsBeforeRetry = client.ServiceCalls;
        await WaitUntilAsync(() => client.ServiceCalls > attemptsBeforeRetry, time);
        await service.StopAsync(CancellationToken.None);

        Assert.True(client.ServiceCalls > attemptsBeforeRetry);
    }

    [Fact]
    public async Task AnInstanceWithoutTheLeaseNeverTouchesTheTuner()
    {
        var client = new FakeMirakurunClient();
        var store = new RecordingEpgStore();
        using EpgSyncHostedService service = CreateService(
            client,
            store,
            out FakeTimeProvider time,
            leaseProvider: new NeverAcquiredLeaseProvider());

        await service.StartAsync(CancellationToken.None);
        time.Advance(TimeSpan.FromMinutes(5));
        await Task.Delay(50);
        await service.StopAsync(CancellationToken.None);

        Assert.Equal(0, client.ServiceCalls);
        Assert.Empty(store.Snapshots);
    }

    private static EpgSyncHostedService CreateService(
        IMirakurunClient client,
        IEpgStore store,
        out FakeTimeProvider timeProvider,
        IEpgSyncLeaseProvider? leaseProvider = null)
    {
        timeProvider = new FakeTimeProvider(Start);
        return new EpgSyncHostedService(
            client,
            new MirakurunEpgMapper(Options.Create(new EpgOptions())),
            store,
            leaseProvider ?? new AlwaysAcquiredLeaseProvider(),
            Options.Create(new MirakurunOptions { BaseUrl = "http://mirakurun.test" }),
            Options.Create(new EpgOptions()),
            timeProvider,
            NullLogger<EpgSyncHostedService>.Instance);
    }

    /// <summary>
    /// 偽の時計は自分では進まないため、条件が満たされるまで少しずつ進めながら待つ。
    /// </summary>
    private static async Task WaitUntilAsync(Func<bool> condition, FakeTimeProvider? time = null)
    {
        for (int attempt = 0; attempt < 200 && !condition(); attempt++)
        {
            time?.Advance(TimeSpan.FromSeconds(5));
            await Task.Delay(20);
        }
    }

    private sealed class FakeMirakurunClient : IMirakurunClient
    {
        private readonly Channel<MirakurunEventDto> events = Channel.CreateUnbounded<MirakurunEventDto>();

        public int ServiceCalls { get; private set; }

        public bool EndEventStreamImmediately { get; init; }

        public Exception? ServicesError { get; init; }

        public ValueTask<IReadOnlyList<MirakurunServiceDto>> GetServicesAsync(CancellationToken cancellationToken)
        {
            ServiceCalls++;
            if (ServicesError is not null)
            {
                throw ServicesError;
            }

            return ValueTask.FromResult<IReadOnlyList<MirakurunServiceDto>>([
                new MirakurunServiceDto
                {
                    Id = 3_273_601_024,
                    ServiceId = 1024,
                    NetworkId = 32736,
                    Name = "ＮＨＫ総合１・東京",
                    Type = 1,
                    HasLogoData = true,
                    RemoteControlKeyId = 1,
                    Channel = new MirakurunChannelDto { Type = "GR", Channel = "27" },
                },
            ]);
        }

        public ValueTask<IReadOnlyList<MirakurunProgramDto>> GetProgramsAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<MirakurunProgramDto>>([
                new MirakurunProgramDto
                {
                    Id = 327_360_102_400_001,
                    EventId = 1,
                    ServiceId = 1024,
                    NetworkId = 32736,
                    StartAt = Start.AddHours(1).ToUnixTimeMilliseconds(),
                    Duration = 3_600_000,
                    IsFree = true,
                    Name = "テスト番組",
                },
            ]);

        public async IAsyncEnumerable<MirakurunEventDto> ReadEventsAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            if (EndEventStreamImmediately)
            {
                yield break;
            }

            await foreach (MirakurunEventDto item in events.Reader.ReadAllAsync(cancellationToken))
            {
                yield return item;
            }
        }
    }

    /// <summary>
    /// 同期は背景で走り続けるため、記録は排他し、判定は写しに対して行う。
    /// </summary>
    private sealed class RecordingEpgStore : IEpgStore
    {
        private readonly Lock gate = new();
        private readonly List<EpgSnapshot> snapshots = [];
        private readonly List<string> failures = [];

        public IReadOnlyList<EpgSnapshot> Snapshots
        {
            get
            {
                lock (gate)
                {
                    return snapshots.ToArray();
                }
            }
        }

        public IReadOnlyList<string> Failures
        {
            get
            {
                lock (gate)
                {
                    return failures.ToArray();
                }
            }
        }

        public TaskCompletionSource SnapshotCommitted { get; } = new();

        public TaskCompletionSource FailureRecorded { get; } = new();

        public ValueTask ReplaceSnapshotAsync(EpgSnapshot snapshot, CancellationToken cancellationToken)
        {
            lock (gate)
            {
                snapshots.Add(snapshot);
            }

            SnapshotCommitted.TrySetResult();
            return ValueTask.CompletedTask;
        }

        public ValueTask ApplyChangesAsync(
            IReadOnlyList<EpgChannel> changedChannels,
            IReadOnlyList<EpgProgram> upsertPrograms,
            IReadOnlyList<long> deleteProgramIds,
            DateTimeOffset streamEventAt,
            CancellationToken cancellationToken) => ValueTask.CompletedTask;

        public ValueTask DeleteProgramsEndingBeforeAsync(
            DateTimeOffset threshold,
            CancellationToken cancellationToken) => ValueTask.CompletedTask;

        public ValueTask RecordSyncFailureAsync(
            DateTimeOffset attemptedAt,
            string failureMessage,
            CancellationToken cancellationToken)
        {
            lock (gate)
            {
                failures.Add(failureMessage);
            }

            FailureRecorded.TrySetResult();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class AlwaysAcquiredLeaseProvider : IEpgSyncLeaseProvider
    {
        public ValueTask<IAsyncDisposable?> TryAcquireAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult<IAsyncDisposable?>(new Lease());

        private sealed class Lease : IAsyncDisposable
        {
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    private sealed class NeverAcquiredLeaseProvider : IEpgSyncLeaseProvider
    {
        public ValueTask<IAsyncDisposable?> TryAcquireAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult<IAsyncDisposable?>(null);
    }
}
