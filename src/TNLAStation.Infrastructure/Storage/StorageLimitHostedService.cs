using System.Diagnostics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TNLAStation.Application.Abstractions;
using TNLAStation.Infrastructure.Configuration;

namespace TNLAStation.Infrastructure.Storage;

/// <summary>
/// 保存先の空き容量を定期的に確認し、閾値を下回ったら保護されていない最も古い録画から
/// 順に消す。<see cref="RecordedDirectoryOptions.LimitThresholdMb"/> を設定した保存先が
/// 1 つも無ければ、確認そのものをしない。
/// </summary>
public sealed partial class StorageLimitHostedService(
    IOptions<StorageOptions> options,
    IRecordedRepository recorded,
    IRecordedItemRepository recordedItems,
    TimeProvider timeProvider,
    ILogger<StorageLimitHostedService> logger,
    Func<string, long>? freeBytesLookup = null) : BackgroundService
{
    private readonly StorageOptions options = options.Value;

    // 試験では実ディスクの空き容量に左右されたくないので、差し替えられるようにする。
    // 既定は本物の DriveInfo 参照。
    private readonly Func<string, long> getFreeBytes = freeBytesLookup ?? DefaultGetFreeBytes;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        RecordedDirectoryOptions[] watched = [.. options.RecordedDirectories.Where(directory => directory.LimitThresholdMb is not null)];
        if (watched.Length == 0)
        {
            return;
        }

        TimeSpan interval = TimeSpan.FromSeconds(Math.Max(5, options.StorageLimitCheckIntervalSeconds));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                foreach (RecordedDirectoryOptions directory in watched)
                {
                    await CheckAsync(directory, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                // 一度失敗しても次の周期で確認し直せる。落とすと空き容量監視自体が止まる。
                LogCheckFailed(logger, exception);
            }

            await Task.Delay(interval, timeProvider, stoppingToken);
        }
    }

    internal async Task CheckAsync(RecordedDirectoryOptions directory, CancellationToken cancellationToken)
    {
        long thresholdBytes = directory.LimitThresholdMb!.Value * 1024 * 1024;
        long free = getFreeBytes(directory.Path);
        if (free > thresholdBytes)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(directory.LimitCmd))
        {
            RunLimitCommand(directory.LimitCmd);
        }

        if (!string.Equals(directory.Action, "remove", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        // 1 件消しては測り直す。まとめて消してから測ると、必要以上に消してしまうことがある。
        while (free <= thresholdBytes)
        {
            long? id = await recorded.FindOldestUnprotectedAsync(cancellationToken);
            if (id is null)
            {
                // 消せるものがもう無い。閾値を下回ったままでも、それ以上はできることがない。
                break;
            }

            await recordedItems.DeleteAsync(id.Value, cancellationToken);
            free = getFreeBytes(directory.Path);
            await Task.Delay(TimeSpan.FromMilliseconds(100), timeProvider, cancellationToken);
        }
    }

    private static long DefaultGetFreeBytes(string path)
    {
        var drive = new DriveInfo(path);
        return Math.Clamp(drive.AvailableFreeSpace, 0, drive.TotalSize);
    }

    private void RunLimitCommand(string command)
    {
        try
        {
            string[] parts = ShellCommandLine.Split(command);
            if (parts.Length == 0)
            {
                return;
            }

            var startInfo = new ProcessStartInfo(parts[0]) { UseShellExecute = false };
            foreach (string argument in parts.Skip(1))
            {
                startInfo.ArgumentList.Add(argument);
            }

            // 完了を待たない。通知コマンドなどを想定しており、遅い/固まるコマンドで空き容量の
            // 監視自体を止めたくない。環境変数は親からそのまま継承する — 上流も limitCmd だけは
            // 「互換性のためここでは全ての環境変数を渡すため env は未指定」と明記して
            // spawn へ env を渡していない (StorageManageModel.check)。
            Process.Start(startInfo)?.Dispose();
        }
        catch (Exception exception)
        {
            LogLimitCmdFailed(logger, command, exception);
        }
    }


    [LoggerMessage(
        EventId = 7000,
        Level = LogLevel.Error,
        Message = "Could not check the free space of a recorded directory")]
    private static partial void LogCheckFailed(ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = 7001,
        Level = LogLevel.Warning,
        Message = "Could not run the storage limit command: {Command}")]
    private static partial void LogLimitCmdFailed(ILogger logger, string command, Exception exception);
}
