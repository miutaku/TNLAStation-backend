using TNLAStation.Application.Abstractions;

namespace TNLAStation.Infrastructure.Persistence;

public sealed class PostgresEpgSyncLeaseProvider(string connectionString) : IEpgSyncLeaseProvider
{
    private const long AdvisoryLockKey = 23_728_404_687_626_567;

    public ValueTask<IAsyncDisposable?> TryAcquireAsync(CancellationToken cancellationToken) =>
        PostgresAdvisoryLease.TryAcquireAsync(connectionString, AdvisoryLockKey, cancellationToken);
}

/// <summary>
/// 予約生成は番組表の同期とは別の鍵を使う。同じ鍵にすると、同期を持っている実体しか
/// 予約を作り直せなくなり、同期が長く続く間ずっと予約が止まる。
/// </summary>
public sealed class PostgresReserveLeaseProvider(string connectionString) : IReserveGenerationLeaseProvider
{
    private const long AdvisoryLockKey = 23_728_404_687_626_568;

    public ValueTask<IAsyncDisposable?> TryAcquireAsync(CancellationToken cancellationToken) =>
        PostgresAdvisoryLease.TryAcquireAsync(connectionString, AdvisoryLockKey, cancellationToken);
}

/// <summary>
/// 録画は録り終わるまで鍵を握り続ける。予約の生成と同じ鍵にすると、録っている間ずっと
/// 予約が作り直されない。
/// </summary>
public sealed class PostgresRecordingLeaseProvider(string connectionString) : IRecordingLeaseProvider
{
    private const long AdvisoryLockKey = 23_728_404_687_626_569;

    public ValueTask<IAsyncDisposable?> TryAcquireAsync(CancellationToken cancellationToken) =>
        PostgresAdvisoryLease.TryAcquireAsync(connectionString, AdvisoryLockKey, cancellationToken);
}
