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
    public void KeyFramesAreForcedEverySecond()
    {
        string[] arguments = HlsArguments.CreateLowLatency("rtsp://host/live/1", 480, "1500k", "128k");

        Assert.Equal("expr:gte(t,n_forced*1)", ValueAfter(arguments, "-force_key_frames"));
        Assert.Equal("zerolatency", ValueAfter(arguments, "-tune"));
    }

    /// <summary>
    /// 放送波は画素が正方形ではない。形で持たせないと、SAR を見ない再生機が 4:3 で映す。
    /// </summary>
    [Fact]
    public void TheOutputIsScaledToSquarePixels()
    {
        string[] arguments = HlsArguments.CreateLowLatency("rtsp://host/live/1", 720, "3000k", "192k");
        string filters = ValueAfter(arguments, "-vf");

        Assert.Equal("yadif,scale=round(720*dar/2)*2:720,setsar=1", filters);
    }

    /// <summary>
    /// 放送の MPEG-2 は B フレームを持つ。復号側の並べ替えを止めると表示順が崩れ、
    /// タイムスタンプは整ったまま映像だけがガクつく。
    /// </summary>
    [Fact]
    public void TheInputDoesNotAskTheDecoderToSkipReordering()
    {
        string[] arguments = HlsArguments.CreateLowLatency("rtsp://host/live/1", 480, "1500k", "128k");
        string[] beforeInput = arguments[..Array.IndexOf(arguments, "-i")];

        Assert.DoesNotContain("low_delay", beforeInput);
        Assert.DoesNotContain(beforeInput, argument => argument.Contains("nobuffer", StringComparison.Ordinal));
    }

    private static string ValueAfter(string[] arguments, string flag) =>
        arguments[Array.LastIndexOf(arguments, flag) + 1];
}
