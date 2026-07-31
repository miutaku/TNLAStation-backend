using TNLAStation.FfmpegWorker.Streaming;

namespace TNLAStation.FfmpegWorker.Tests;

public sealed class LowLatencyArgumentsTests
{
    [Fact]
    public void ThePublishUrlIsTheLastArgumentAndTheOutputIsRtspOverTcp()
    {
        string[] arguments = HlsArguments.CreateLowLatency(
            "rtsp://mediamtx:8554/live/7",
            height: 720,
            videoBitrate: "3000k",
            audioBitrate: "192k");

        Assert.Equal("rtsp://mediamtx:8554/live/7", arguments[^1]);
        Assert.Equal("rtsp", ValueAfter(arguments, "-f"));
        Assert.Equal("tcp", ValueAfter(arguments, "-rtsp_transport"));
    }

    /// <summary>
    /// セグメントを切るのは配信サーバーなので、切れ目にキーフレームが無いと繋ぎ目が崩れる。
    /// </summary>
    [Fact]
    public void KeyFramesAreForcedEverySecondAndBufferingIsOff()
    {
        string[] arguments = HlsArguments.CreateLowLatency("rtsp://host/live/1", 480, "1500k", "128k");

        Assert.Equal("expr:gte(t,n_forced*1)", ValueAfter(arguments, "-force_key_frames"));
        Assert.Equal("zerolatency", ValueAfter(arguments, "-tune"));
        Assert.Equal("+discardcorrupt+nobuffer", ValueAfter(arguments, "-fflags"));
    }

    private static string ValueAfter(string[] arguments, string flag) =>
        arguments[Array.LastIndexOf(arguments, flag) + 1];
}
