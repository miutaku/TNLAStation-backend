using Microsoft.Extensions.Options;
using TNLAStation.FfmpegWorker.Options;

namespace TNLAStation.FfmpegWorker.Streaming;

/// <summary>
/// backend から観測されなくなった HLS セッションの後始末。backend は生きたセッションの
/// 状態を 10 秒ごとに見に来るので、長く観測が無いのは backend がそのセッションを忘れた
/// (掃除 DELETE が届かなかった・backend が再起動した) 合図。放置すると ffmpeg が
/// チューナー・CPU・帯域を掴んだまま、どの一覧からも見えない孤児になる。
/// </summary>
internal sealed partial class UnobservedSessionReaper(
    HlsSessionRegistry registry,
    IOptions<FfmpegOptions> options,
    TimeProvider timeProvider,
    ILogger<UnobservedSessionReaper> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        TimeSpan timeout = TimeSpan.FromSeconds(Math.Max(30, options.Value.SessionUnobservedTimeoutSeconds));
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(15), timeProvider);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                foreach (long streamId in await registry.StopUnobservedAsync(timeProvider.GetUtcNow() - timeout))
                {
                    LogSessionReaped(logger, streamId, timeout.TotalSeconds);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    [LoggerMessage(
        EventId = 9000,
        Level = LogLevel.Warning,
        Message = "Stopped stream {StreamId}: the backend has not checked on it for {TimeoutSeconds}s")]
    private static partial void LogSessionReaped(ILogger logger, long streamId, double timeoutSeconds);
}
