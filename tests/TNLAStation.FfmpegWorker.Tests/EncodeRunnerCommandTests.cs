using TNLAStation.FfmpegWorker.Media;

namespace TNLAStation.FfmpegWorker.Tests;

public sealed class EncodeRunnerCommandTests
{
    [Fact]
    public void PreservesSpacesInPlaceholderPaths()
    {
        string[] command = EncodeRunner.SplitCommand(
            "%FFMPEG% -i %INPUT% -y %OUTPUT%",
            "/recorded/番組 タイトル.m2ts",
            "/recorded/番組 タイトル-H.264.part.mp4",
            "/usr/bin/ffmpeg",
            "/usr/bin/ffprobe");

        Assert.Equal(
            [
                "/usr/bin/ffmpeg",
                "-i",
                "/recorded/番組 タイトル.m2ts",
                "-y",
                "/recorded/番組 タイトル-H.264.part.mp4",
            ],
            command);
    }

    [Fact]
    public void PreservesQuotedPlaceholdersWithoutAddingLiteralQuotes()
    {
        string[] command = EncodeRunner.SplitCommand(
            "\"%FFMPEG%\" -i \"%INPUT%\" -y \"%OUTPUT%\"",
            "/recorded/番組 タイトル.m2ts",
            "/recorded/番組 タイトル.mp4",
            "/opt/ffmpeg tools/ffmpeg",
            "/opt/ffmpeg tools/ffprobe");

        Assert.Equal("/opt/ffmpeg tools/ffmpeg", command[0]);
        Assert.Equal("/recorded/番組 タイトル.m2ts", command[2]);
        Assert.Equal("/recorded/番組 タイトル.mp4", command[4]);
    }
}
