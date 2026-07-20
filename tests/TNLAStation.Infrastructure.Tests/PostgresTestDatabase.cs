using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using TNLAStation.Infrastructure.Persistence;

namespace TNLAStation.Infrastructure.Tests;

/// <summary>
/// 試験ごとに専用のデータベースを作り、migration を当ててから使う。共有しないので、
/// 並列実行しても互いのデータを壊さず、schema そのものも毎回検証できる。
/// </summary>
public sealed class PostgresTestDatabase : IAsyncDisposable
{
    private readonly string databaseName;
    private readonly string adminConnectionString;
    private readonly ServiceProvider services;

    private PostgresTestDatabase(string adminConnectionString, string databaseName, string connectionString)
    {
        this.adminConnectionString = adminConnectionString;
        this.databaseName = databaseName;
        ConnectionString = connectionString;
        services = new ServiceCollection()
            .AddDbContextFactory<EpgDbContext>(options => options.UseNpgsql(connectionString))
            .BuildServiceProvider();
        ContextFactory = services.GetRequiredService<IDbContextFactory<EpgDbContext>>();
    }

    public string ConnectionString { get; }

    public IDbContextFactory<EpgDbContext> ContextFactory { get; }

    public static async Task<PostgresTestDatabase> CreateAsync()
    {
        string adminConnectionString = PostgresFactAttribute.ConnectionString
            ?? throw new InvalidOperationException("PostgreSQL の接続文字列が設定されていません。");
        string databaseName = $"tnla_test_{Guid.NewGuid():N}";

        await using (var connection = new NpgsqlConnection(adminConnectionString))
        {
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand($"""CREATE DATABASE "{databaseName}" """, connection);
            await command.ExecuteNonQueryAsync();
        }

        var builder = new NpgsqlConnectionStringBuilder(adminConnectionString) { Database = databaseName };
        var database = new PostgresTestDatabase(adminConnectionString, databaseName, builder.ConnectionString);

        await using EpgDbContext context = await database.ContextFactory.CreateDbContextAsync();
        await context.Database.MigrateAsync();

        return database;
    }

    public async ValueTask DisposeAsync()
    {
        await services.DisposeAsync();
        NpgsqlConnection.ClearAllPools();

        await using var connection = new NpgsqlConnection(adminConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            $"""DROP DATABASE IF EXISTS "{databaseName}" WITH (FORCE)""",
            connection);
        await command.ExecuteNonQueryAsync();
    }
}
