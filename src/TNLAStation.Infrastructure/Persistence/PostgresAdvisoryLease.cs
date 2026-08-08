using Npgsql;
using TNLAStation.Application.Abstractions;

namespace TNLAStation.Infrastructure.Persistence;

/// <summary>
/// PostgreSQL の advisory lock で「この仕事をするのは 1 つだけ」を作る。鍵ごとに別の仕事に
/// なるので、番組表の同期と予約の生成は互いを待たない。
/// </summary>
internal static class PostgresAdvisoryLease
{
    public static async ValueTask<IAsyncDisposable?> TryAcquireAsync(
        string connectionString,
        long lockKey,
        CancellationToken cancellationToken)
    {
        var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand("SELECT pg_try_advisory_lock(@lock_key)", connection);
        command.Parameters.AddWithValue("lock_key", lockKey);
        object? result = await command.ExecuteScalarAsync(cancellationToken);
        if (result is true)
        {
            return new Lease(connection, lockKey);
        }

        await connection.DisposeAsync();
        return null;
    }

    private sealed class Lease(NpgsqlConnection connection, long lockKey)
        : IAsyncDisposable, IRecordingLeaseHealth
    {
        private bool disposed;

        public async ValueTask<bool> IsAliveAsync(CancellationToken cancellationToken)
        {
            if (disposed)
            {
                return false;
            }

            try
            {
                await using var command = new NpgsqlCommand("SELECT 1", connection);
                await command.ExecuteScalarAsync(cancellationToken);
                return true;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (NpgsqlException)
            {
                return false;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            try
            {
                await using var command = new NpgsqlCommand("SELECT pg_advisory_unlock(@lock_key)", connection);
                command.Parameters.AddWithValue("lock_key", lockKey);
                await command.ExecuteNonQueryAsync();
            }
            catch (NpgsqlException)
            {
                // 接続断なら PostgreSQL 側の session lock は既に解放されている。
            }
            catch (InvalidOperationException)
            {
                // 壊れた接続を閉じればよく、unlock の再試行はできない。
            }
            finally
            {
                await connection.DisposeAsync();
            }
        }
    }
}
