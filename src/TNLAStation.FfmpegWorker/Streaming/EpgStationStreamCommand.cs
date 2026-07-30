using System.Globalization;
using TNLAStation.FfmpegWorker.Options;

namespace TNLAStation.FfmpegWorker.Streaming;

internal sealed record ProcessCommand(string FileName, string[] Arguments);

internal static class EpgStationStreamCommand
{
    private const string FfmpegPlaceholder = "__TNLA_FFMPEG__";
    private const string InputPlaceholder = "__TNLA_INPUT__";
    private const string OutputPlaceholder = "__TNLA_OUTPUT__";
    private const string StreamFileDirectoryPlaceholder = "__TNLA_STREAM_FILE_DIRECTORY__";
    private const string StreamIdPlaceholder = "__TNLA_STREAM_ID__";
    private const string SeekPlaceholder = "__TNLA_SEEK__";

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
            .Replace("%FFMPEG%", FfmpegPlaceholder, StringComparison.Ordinal)
            .Replace("%INPUT%", InputPlaceholder, StringComparison.Ordinal)
            .Replace("%OUTPUT%", OutputPlaceholder, StringComparison.Ordinal)
            .Replace("%streamFileDir%", StreamFileDirectoryPlaceholder, StringComparison.Ordinal)
            .Replace("%streamNum%", StreamIdPlaceholder, StringComparison.Ordinal)
            .Replace(
                "%SS%",
                transportStream
                    ? SeekPlaceholder
                    : playPosition?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                StringComparison.Ordinal)
            .Replace("%SPACE%", " ", StringComparison.Ordinal);
        string[] parts = ShellCommandLine.Split(expanded);
        if (parts.Length == 0)
        {
            throw new InvalidOperationException("StreamProcessStartFailed");
        }

        var resolved = new List<string>(parts.Length);
        foreach (string part in parts)
        {
            bool isTransportSeek = transportStream &&
                string.Equals(part, SeekPlaceholder, StringComparison.Ordinal);
            if (isTransportSeek &&
                resolved.Count > 0 &&
                string.Equals(resolved[^1], "-ss", StringComparison.Ordinal))
            {
                resolved.RemoveAt(resolved.Count - 1);
                continue;
            }

            if (isTransportSeek)
            {
                continue;
            }

            resolved.Add(part
                .Replace(FfmpegPlaceholder, options.FfmpegPath, StringComparison.Ordinal)
                .Replace(InputPlaceholder, input, StringComparison.Ordinal)
                .Replace(OutputPlaceholder, output, StringComparison.Ordinal)
                .Replace(StreamFileDirectoryPlaceholder, options.WorkDirectory, StringComparison.Ordinal)
                .Replace(
                    StreamIdPlaceholder,
                    streamId?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                    StringComparison.Ordinal));
        }

        if (resolved.Count == 0)
        {
            throw new InvalidOperationException("StreamProcessStartFailed");
        }

        return new ProcessCommand(resolved[0], [.. resolved.Skip(1)]);
    }
}
