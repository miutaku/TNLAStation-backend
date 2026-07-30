using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TNLAStation.Application.Abstractions;
using TNLAStation.Infrastructure.Persistence;

namespace TNLAStation.Infrastructure.Transcoding;

/// <summary>
/// Workerが直接DBを更新しても、APIに接続したclientへ進捗更新を通知する。
/// </summary>
public sealed partial class EncodeQueueNotificationService(
    IDbContextFactory<EpgDbContext> contextFactory,
    IClientNotifier notifier,
    TimeProvider timeProvider,
    ILogger<EncodeQueueNotificationService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        string? previous = null;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                string current = await ReadStateAsync(stoppingToken);
                if (previous is not null && !string.Equals(previous, current, StringComparison.Ordinal))
                {
                    notifier.NotifyUpdateEncodeProgress();
                    notifier.NotifyClient();
                }

                previous = current;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                LogPollingFailed(logger, exception);
            }

            await Task.Delay(TimeSpan.FromSeconds(1), timeProvider, stoppingToken);
        }
    }

    private async Task<string> ReadStateAsync(CancellationToken cancellationToken)
    {
        await using EpgDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);
        string[] rows = await context.EncodeTasks.AsNoTracking()
            .OrderBy(task => task.Id)
            .Select(task =>
                task.Id + ":" +
                task.Status + ":" +
                task.Percent + ":" +
                task.Log + ":" +
                task.CancelRequested)
            .ToArrayAsync(cancellationToken);
        return string.Join('\n', rows);
    }

    [LoggerMessage(
        EventId = 5010,
        Level = LogLevel.Warning,
        Message = "Could not poll the encode queue for client notifications")]
    private static partial void LogPollingFailed(ILogger logger, Exception exception);
}
