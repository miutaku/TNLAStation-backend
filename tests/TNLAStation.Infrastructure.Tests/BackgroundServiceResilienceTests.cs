using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using TNLAStation.Application.Abstractions;
using TNLAStation.Application.Models;
using TNLAStation.Domain;
using TNLAStation.Infrastructure.Configuration;
using TNLAStation.Infrastructure.Mirakurun;
using TNLAStation.Infrastructure.Persistence;
using TNLAStation.Infrastructure.Recording;
using TNLAStation.Infrastructure.Transcoding;

namespace TNLAStation.Infrastructure.Tests;

/// <summary>
/// データベースが一時的に落ちたときの、裏で動く処理の粘り。DB は再起動の順番やネットワークの
/// 瞬断で普通に落ちる。落ちたときに録画やエンコードの土台ごと死んで、DB が戻っても復帰
/// しないと、丸ごと再起動するまで録画が止まる。時計を差し替えて実時間を待たずに進める。
/// </summary>
public sealed class BackgroundServiceResilienceTests
{
    private static readonly DateTimeOffset Start = new(2026, 7, 20, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task TheRecordingSchedulerSurvivesALeaseFailureAndKeepsTrying()
    {
        // 鍵を取ろうとするたびに例外。DB へ繋げない状態そのもの。
        var lease = new FlakyLeaseProvider(throwUntilAttempt: int.MaxValue);
        using RecordingScheduler scheduler = CreateScheduler(lease, out FakeTimeProvider time);

        await scheduler.StartAsync(CancellationToken.None);
        // 例外で落ちていれば、2 回目の試行は来ない。来ることをもって生き残りを確かめる。
        await WaitUntilAsync(() => lease.Attempts >= 3, time);
        await scheduler.StopAsync(CancellationToken.None);

        Assert.True(lease.Attempts >= 3);
    }

    [Fact]
    public async Task TheRecordingSchedulerRecoversOnceTheDatabaseComesBack()
    {
        // 最初の 2 回は落ちて、3 回目から鍵が取れる。DB が戻った後に、掴んだままにせず
        // 録画へ進めることを確かめる。
        var lease = new FlakyLeaseProvider(throwUntilAttempt: 2);
        using RecordingScheduler scheduler = CreateScheduler(lease, out FakeTimeProvider time);

        await scheduler.StartAsync(CancellationToken.None);
        await WaitUntilAsync(() => lease.Acquired > 0, time);
        await scheduler.StopAsync(CancellationToken.None);

        Assert.True(lease.Acquired > 0);
    }

    [Fact]
    public async Task TheEncodeWorkerSurvivesADatabaseOutageAtStartup()
    {
        // 起動直後の「実行中を待ちへ戻す」で DB が落ちている。ここで落ちると二度と
        // エンコードが動かないので、生き残って試し続けることを確かめる。
        var contextFactory = new ThrowingContextFactory();
        var lease = new FlakyLeaseProvider(throwUntilAttempt: 0);
        var time = new FakeTimeProvider(Start);
        using var worker = new EncodeWorker(
            contextFactory,
            Unused.Instance,
            Unused.Instance,
            Unused.Instance,
            Unused.Instance,
            Unused.Instance,
            Unused.Instance,
            Options.Create(new EncodeOptions { PollIntervalSeconds = 1 }),
            Options.Create(new TNLAStation.Infrastructure.Configuration.CommandHookOptions()),
            lease,
            new EncodeJobRegistry(),
            NullClientNotifier.Instance,
            time,
            NullLogger<EncodeWorker>.Instance);

        await worker.StartAsync(CancellationToken.None);
        await WaitUntilAsync(() => contextFactory.Attempts >= 3, time);
        await worker.StopAsync(CancellationToken.None);

        Assert.True(contextFactory.Attempts >= 3);
    }

    private static RecordingScheduler CreateScheduler(
        IRecordingLeaseProvider lease,
        out FakeTimeProvider time)
    {
        time = new FakeTimeProvider(Start);
        return new RecordingScheduler(
            Unused.Instance,
            Unused.Instance,
            Unused.Instance,
            Unused.Instance,
            Unused.Instance,
            Unused.Instance,
            Unused.Instance,
            NullClientNotifier.Instance,
            new RecordingRecovery(Unused.Instance, NullLogger<RecordingRecovery>.Instance),
            Options.Create(new RecordingOptions { PollIntervalSeconds = 1 }),
            Options.Create(new StorageOptions()),
            Options.Create(new TNLAStation.Infrastructure.Mirakurun.MirakurunOptions()),
            Options.Create(new TNLAStation.Infrastructure.Configuration.CommandHookOptions()),
            lease,
            new RecordingJobRegistry(),
            new RecordingScheduleSignal(),
            time,
            NullLogger<RecordingScheduler>.Instance);
    }

    private static async Task WaitUntilAsync(Func<bool> condition, FakeTimeProvider? time = null)
    {
        for (int attempt = 0; attempt < 400 && !condition(); attempt++)
        {
            time?.Advance(TimeSpan.FromSeconds(2));
            await Task.Delay(10);
        }
    }

    /// <summary>
    /// 決めた回数までは例外を投げ、その後は鍵を渡す。DB が落ちて戻る様子をなぞる。
    /// </summary>
    private sealed class FlakyLeaseProvider(int throwUntilAttempt)
        : IRecordingLeaseProvider, IReserveGenerationLeaseProvider
    {
        private int attempts;
        private int acquired;

        public int Attempts => Volatile.Read(ref attempts);

        public int Acquired => Volatile.Read(ref acquired);

        public ValueTask<IAsyncDisposable?> TryAcquireAsync(CancellationToken cancellationToken)
        {
            int attempt = Interlocked.Increment(ref attempts);
            if (attempt <= throwUntilAttempt)
            {
                throw new InvalidOperationException("database is unreachable");
            }

            Interlocked.Increment(ref acquired);
            return ValueTask.FromResult<IAsyncDisposable?>(new Lease());
        }

        private sealed class Lease : IAsyncDisposable
        {
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    /// <summary>
    /// どの口を叩いても DB へ繋げない。エンコードの起動処理が最初に触るのがこれ。
    /// </summary>
    private sealed class ThrowingContextFactory : IDbContextFactory<EpgDbContext>
    {
        private int attempts;

        public int Attempts => Volatile.Read(ref attempts);

        public EpgDbContext CreateDbContext()
        {
            Interlocked.Increment(ref attempts);
            throw new InvalidOperationException("database is unreachable");
        }

        public Task<EpgDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref attempts);
            throw new InvalidOperationException("database is unreachable");
        }
    }
}
