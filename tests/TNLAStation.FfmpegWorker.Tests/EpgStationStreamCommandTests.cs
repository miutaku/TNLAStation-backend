using TNLAStation.FfmpegWorker.Options;
using TNLAStation.FfmpegWorker.Streaming;

namespace TNLAStation.FfmpegWorker.Tests;

public sealed class EpgStationStreamCommandTests
{
    [Fact]
    public void ExpandsEpgStationLiveHlsPlaceholders()
    {
        var options = new FfmpegOptions
        {
            FfmpegPath = "/usr/local/bin/ffmpeg",
            WorkDirectory = "/work path",
        };

        ProcessCommand command = EpgStationStreamCommand.Expand(
            "\"%FFMPEG%\" -i pipe:0 -hls_segment_filename \"%streamFileDir%/stream%streamNum%-%d.ts\" \"%OUTPUT%\"",
            options,
            "pipe:0",
            "/work path/stream42.m3u8",
            streamId: 42);

        Assert.Equal("/usr/local/bin/ffmpeg", command.FileName);
        Assert.Contains("/work path/stream42-%d.ts", command.Arguments);
        Assert.Equal("/work path/stream42.m3u8", command.Arguments[^1]);
    }

    [Fact]
    public void TransportStreamRemovesSsAndEncodedFileExpandsIt()
    {
        var options = new FfmpegOptions { FfmpegPath = "ffmpeg", WorkDirectory = "/tmp" };

        ProcessCommand ts = EpgStationStreamCommand.Expand(
            "%FFMPEG% %SS% -i %INPUT% pipe:1",
            options,
            "pipe:0",
            "pipe:1",
            playPosition: 12.5,
            transportStream: true);
        ProcessCommand encoded = EpgStationStreamCommand.Expand(
            "%FFMPEG% -ss %SS% -i \"%INPUT%\" pipe:1",
            options,
            "/recorded/a b.mp4",
            "pipe:1",
            playPosition: 12.5);

        Assert.Equal(["-i", "pipe:0", "pipe:1"], ts.Arguments);
        Assert.Contains("12.5", encoded.Arguments);
        Assert.Contains("/recorded/a b.mp4", encoded.Arguments);
    }
}
