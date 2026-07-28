using System.Globalization;
using TNLAStation.FfmpegWorker.Options;

namespace TNLAStation.FfmpegWorker.Streaming;

internal sealed record ProcessCommand(string FileName, string[] Arguments);

internal static class EpgStationStreamCommand
{
    public static ProcessCommand Expand(
        string command,
        FfmpegOptions options,
        string input,
        string output,
        long? streamId = null,
        double? playPosition = null,
        bool transportStream = false)
    {
        string expanded = command
            .Replace("%FFMPEG%", options.FfmpegPath, StringComparison.Ordinal)
            .Replace("%INPUT%", input, StringComparison.Ordinal)
            .Replace("%OUTPUT%", output, StringComparison.Ordinal)
            .Replace("%streamFileDir%", options.WorkDirectory, StringComparison.Ordinal)
            .Replace("%streamNum%", streamId?.ToString(CultureInfo.InvariantCulture) ?? string.Empty, StringComparison.Ordinal)
            .Replace("%SS%", transportStream ? string.Empty : playPosition?.ToString(CultureInfo.InvariantCulture) ?? string.Empty, StringComparison.Ordinal)
            .Replace("%SPACE%", " ", StringComparison.Ordinal);
        string[] parts = ShellCommandLine.Split(expanded);
        if (parts.Length == 0)
        {
            throw new InvalidOperationException("StreamProcessStartFailed");
        }

        return new ProcessCommand(parts[0], parts[1..]);
    }
}
