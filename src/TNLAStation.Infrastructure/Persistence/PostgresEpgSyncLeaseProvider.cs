using Npgsql;
using TNLAStation.Application.Abstractions;

namespace TNLAStation.Infrastructure.Persistence;

public sealed class PostgresEpgSyncLeaseProvider(string connectionString) : IEpgSyncLeaseProvider
{
    private const long AdvisoryLockKey = 23_728_404_687_626_567;

    public async ValueTask<IAsyncDisposable?> TryAcquireAsync(CancellationToken cancellationToken)
    {
        var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            "SELECT pg_try_advisory_lock(@lock_key)",
            connection);
        command.Parameters.AddWithValue("lock_key", AdvisoryLockKey);
        object? result = await command.ExecuteScalarAsync(cancellationToken);
        if (result is true)
        {
            return new Lease(connection);
        }

        await connection.DisposeAsync();
        return null;
    }

    private sealed class Lease(NpgsqlConnection connection) : IAsyncDisposable
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
                await using var command = new NpgsqlCommand(
                    "SELECT pg_advisory_unlock(@lock_key)",
                    connection);
                command.Parameters.AddWithValue("lock_key", AdvisoryLockKey);
                await command.ExecuteNonQueryAsync();
            }
            finally
            {
                await connection.DisposeAsync();
            }
        }
    }
}
