using Npgsql;

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

    private sealed class Lease(NpgsqlConnection connection, long lockKey) : IAsyncDisposable
    {
        private bool disposed;

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
            finally
            {
                await connection.DisposeAsync();
            }
        }
    }
}
