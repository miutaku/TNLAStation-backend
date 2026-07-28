using TNLAStation.Application.Abstractions;

namespace TNLAStation.Infrastructure.Repositories;

/// <summary>
/// GET /api/version が返す値。TNLAStation は EPGStation へ接続しないが、既存クライアントは
/// この値で使える API を判断するため、互換基準である v2.10.0 を名乗る。稼働中の EPGStation の
/// バージョンではなく、TNLAStation が満たす互換仕様のバージョンを示す。
/// </summary>
public sealed class MockVersionRepository : IVersionRepository
{
    public ValueTask<string> GetAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult("2.10.0");
    }
}
