using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TNLAStation.Infrastructure.Persistence;
using TNLAStation.Migrator;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
string? connectionString = builder.Configuration.GetConnectionString("PostgreSQL");
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException(
        "ConnectionStrings:PostgreSQL is required to run database migrations.");
}

builder.Services.AddDbContext<EpgDbContext>(options => options.UseNpgsql(
    connectionString,
    npgsql => npgsql.MigrationsAssembly(typeof(EpgDbContext).Assembly.FullName)));

using IHost host = builder.Build();
await using AsyncServiceScope scope = host.Services.CreateAsyncScope();
EpgDbContext context = scope.ServiceProvider.GetRequiredService<EpgDbContext>();
await context.Database.MigrateAsync();

ILogger<Program> logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
MigratorLog.MigrationsCompleted(logger);

public partial class Program;
