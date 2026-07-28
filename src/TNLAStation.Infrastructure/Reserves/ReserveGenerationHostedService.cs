using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TNLAStation.Application.Abstractions;
using TNLAStation.Infrastructure.Configuration;

namespace TNLAStation.Infrastructure.Reserves;

/// <summary>
/// 予約を定期的に作り直す。番組表は放送直前まで動くので、一度作った予約をそのままにすると
/// 繰り上がった番組を録り逃す。
/// </summary>
public sealed partial class ReserveGenerationHostedService(
    ReserveGenerator generator,
    IReserveGenerationLeaseProvider leaseProvider,
    IOptions<ReserveOptions> options,
    TimeProvider timeProvider,
    ILogger<ReserveGenerationHostedService> logger) : BackgroundService
{
    private readonly ReserveOptions options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        TimeSpan interval = TimeSpan.FromMinutes(Math.Max(1, options.UpdateIntervalMinutes));

        while (!stoppingToken.IsCancellationRequested)
        {
            // 鍵は生成している間だけ持つ。待っている間も握っていると、頼まれた作り直しも
            // 次の周期が来るまで動けない。
            try
            {
                await using IAsyncDisposable? lease = await leaseProvider.TryAcquireAsync(stoppingToken);
                if (lease is not null)
                {
                    await generator.RunAsync(stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                // 一度失敗しても次の周期で作り直せる。落とすと予約が止まったままになる。
                LogGenerationFailed(logger, exception);
            }

            await Task.Delay(interval, timeProvider, stoppingToken);
        }
    }

    [LoggerMessage(
        EventId = 3010,
        Level = LogLevel.Error,
        Message = "Could not regenerate the reserves; the previous ones stay in place")]
    private static partial void LogGenerationFailed(ILogger logger, Exception exception);
}
