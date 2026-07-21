using System.Diagnostics;
using System.Globalization;
using Microsoft.Extensions.Options;
using TNLAStation.Application.Abstractions;
using TNLAStation.Infrastructure.Configuration;

namespace TNLAStation.Infrastructure.Streaming;

public sealed class FfprobeMediaProbe(IOptions<StreamingOptions> options) : IMediaProbe
{
    private readonly StreamingOptions options = options.Value;

    public async ValueTask<double?> GetDurationSecondsAsync(string path, CancellationToken cancellationToken)
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

        string output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        return double.TryParse(output.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double seconds)
            ? seconds
            : null;
    }
}
