using System.ComponentModel;
using System.Diagnostics;

namespace TNLAStation.FfmpegWorker.Processes;

/// <summary>
/// ffmpeg / ffprobe の寿命を所有元と揃える。<see cref="Process.Dispose()"/> は OS 上の
/// プロセスを終了しないため、破棄前にツリーへ停止を送り、終了を待って回収する。
/// </summary>
internal static class ChildProcessLifetime
{
    private static readonly TimeSpan DefaultStopTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan LastChanceTimeout = TimeSpan.FromSeconds(2);

    /// <summary>
    /// 実行中なら子孫を含めて停止し、終了を回収する。時間内に終了を確認できたら true。
    /// </summary>
    public static async ValueTask<bool> StopAsync(Process process, TimeSpan? timeout = null)
    {
        ArgumentNullException.ThrowIfNull(process);

        if (TryHasExited(process))
        {
            return await WaitForExitAsync(process, TimeSpan.Zero);
        }

        if (!TryKill(process, entireProcessTree: true))
        {
            TryKill(process, entireProcessTree: false);
        }

        if (await WaitForExitAsync(process, timeout ?? DefaultStopTimeout))
        {
            return true;
        }

        // ツリー停止が一部の環境で効かなかった場合の最後の手段。親だけでももう一度
        // 停止し、ハンドルを掴んだまま返さない。
        TryKill(process, entireProcessTree: false);
        return await WaitForExitAsync(process, LastChanceTimeout);
    }

    private static bool TryKill(Process process, bool entireProcessTree)
    {
        try
        {
            if (process.HasExited)
            {
                return true;
            }

            process.Kill(entireProcessTree);
            return true;
        }
        catch (InvalidOperationException)
        {
            return TryHasExited(process);
        }
        catch (NotSupportedException)
        {
            return false;
        }
        catch (Win32Exception)
        {
            return TryHasExited(process);
        }
    }

    private static bool TryHasExited(Process process)
    {
        try
        {
            return process.HasExited;
        }
        catch (InvalidOperationException)
        {
            return true;
        }
        catch (Win32Exception)
        {
            return false;
        }
    }

    private static async ValueTask<bool> WaitForExitAsync(Process process, TimeSpan timeout)
    {
        try
        {
            if (timeout == TimeSpan.Zero)
            {
                await process.WaitForExitAsync();
            }
            else
            {
                await process.WaitForExitAsync().WaitAsync(timeout);
            }

            return true;
        }
        catch (InvalidOperationException)
        {
            return TryHasExited(process);
        }
        catch (TimeoutException)
        {
            return TryHasExited(process);
        }
        catch (Win32Exception)
        {
            return TryHasExited(process);
        }
    }
}
