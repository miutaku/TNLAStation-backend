using System.Diagnostics;
using System.Globalization;
using System.Runtime.ExceptionServices;
using Microsoft.Extensions.Options;
using TNLAStation.FfmpegWorker.Options;
using TNLAStation.FfmpegWorker.Processes;

namespace TNLAStation.FfmpegWorker.Media;

public sealed class MediaProbeRunner(IOptions<FfmpegOptions> options, ProcessGate gate)
{
    private readonly FfmpegOptions options = options.Value;

    public async ValueTask<double?> GetDurationSecondsAsync(string path, CancellationToken cancellationToken)
    {
        await using ProcessLease lease = await gate.AcquireAsync(ProcessPriority.Background, cancellationToken);
        return await ProbeAsync(path, cancellationToken);
    }

    /// <summary>枠を既に持っている呼び出し用。二重に取ると定員 1 の構成で自分と競合する。</summary>
    public async ValueTask<double?> ProbeAsync(string path, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo(options.FfprobePath)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (string argument in new[]
        {
            "-v", "error",
            "-show_entries", "format=duration",
            "-of", "default=noprint_wrappers=1:nokey=1",
            path,
        })
        {
            startInfo.ArgumentList.Add(argument);
        }

        using Process? process = Process.Start(startInfo);
        if (process is null)
        {
            return null;
        }

        Task<string> outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        Task<string> errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        bool stopped;
        Exception? failure = null;
        try
        {
            await Task.WhenAll(
                outputTask,
                errorTask,
                process.WaitForExitAsync(cancellationToken));
        }
        catch (Exception exception)
        {
            failure = exception;
        }
        finally
        {
            stopped = await ChildProcessLifetime.StopAsync(process);
        }

        if (!stopped)
        {
            throw new InvalidOperationException("ProbeProcessDidNotExit");
        }

        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }

        string output = await outputTask;
        return double.TryParse(output.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double seconds)
            ? seconds
            : null;
    }
}
