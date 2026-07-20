using Microsoft.Extensions.Logging;

namespace TNLAStation.Migrator;

internal static partial class MigratorLog
{
    [LoggerMessage(
        EventId = 3000,
        Level = LogLevel.Information,
        Message = "TNLAStation database migrations completed successfully.")]
    public static partial void MigrationsCompleted(ILogger logger);
}
