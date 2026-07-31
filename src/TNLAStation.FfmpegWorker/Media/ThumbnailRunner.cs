using System.Diagnostics;
using System.Globalization;
using System.Runtime.ExceptionServices;
using Microsoft.Extensions.Options;
using TNLAStation.FfmpegWorker.Options;
using TNLAStation.FfmpegWorker.Processes;

namespace TNLAStation.FfmpegWorker.Media;

/// <summary>
/// EPGStation の thumbnailPosition と同じく、常に指定秒数の位置から 1 枚切り出す。
/// 動画の長さに応じて位置を調整するようなことはしない — 短い録画で失敗するのも含めて
/// EPGStation と同じ挙動にする。
/// </summary>
public sealed class ThumbnailRunner(IOptions<FfmpegOptions> options, ProcessGate gate)
{
    private readonly FfmpegOptions options = options.Value;

    public async Task<bool> ExtractAsync(
        string input,
        string output,
        int width,
        int? height,
        double positionSeconds,
        string? command,
        CancellationToken cancellationToken)
    {
        await using IAsyncDisposable lease = await gate.AcquireAsync(cancellationToken);

        Directory.CreateDirectory(Path.GetDirectoryName(output) is { Length: > 0 } directory ? directory : ".");

        // 高さ指定が無ければ幅だけ合わせてアスペクト比を保つ (-2)。指定があれば
        // EPGStation の thumbnailSize (幅x高さ) と同じく、比率を崩してでも指定通りにする。
        string heightToken = height?.ToString(CultureInfo.InvariantCulture) ?? "-2";
        string scale = $"scale={width.ToString(CultureInfo.InvariantCulture)}:{heightToken}";

        var startInfo = new ProcessStartInfo
        {
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        if (string.IsNullOrWhiteSpace(command))
        {
            startInfo.FileName = options.FfmpegPath;
            foreach (string argument in new[]
            {
                "-hide_banner",
                "-loglevel", "error",
                "-ss", positionSeconds.ToString("0.###", CultureInfo.InvariantCulture),
                "-i", input,
                "-frames:v", "1",
                "-vf", $"yadif,{scale}",
                "-y", output,
            })
            {
                startInfo.ArgumentList.Add(argument);
            }
        }
        else
        {
            string thumbnailSize = $"{width.ToString(CultureInfo.InvariantCulture)}x{heightToken}";
            string[] parts = ShellCommandLine.Split(Substitute(command, input, output, positionSeconds, thumbnailSize));
            if (parts.Length == 0)
            {
                return false;
            }

            startInfo.FileName = parts[0];
            foreach (string argument in parts.Skip(1))
            {
                startInfo.ArgumentList.Add(argument);
            }
        }

        using Process? process = Process.Start(startInfo);
        if (process is null)
        {
            return false;
        }

        Task<string> errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        bool stopped;
        Exception? failure = null;
        try
        {
            await Task.WhenAll(errorTask, process.WaitForExitAsync(cancellationToken));
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
            throw new InvalidOperationException("ThumbnailProcessDidNotExit");
        }

        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }

        return process.ExitCode == 0 && File.Exists(output);
    }

    private string Substitute(string command, string input, string output, double positionSeconds, string thumbnailSize) => command
        .Replace("%FFMPEG%", options.FfmpegPath, StringComparison.Ordinal)
        .Replace("%INPUT%", input, StringComparison.Ordinal)
        .Replace("%OUTPUT%", output, StringComparison.Ordinal)
        .Replace("%THUMBNAIL_POSITION%", positionSeconds.ToString("0.###", CultureInfo.InvariantCulture), StringComparison.Ordinal)
        .Replace("%THUMBNAIL_SIZE%", thumbnailSize, StringComparison.Ordinal);
}
