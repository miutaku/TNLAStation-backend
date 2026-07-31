using TNLAStation.FfmpegWorker;

namespace TNLAStation.FfmpegWorker.Tests;

public sealed class ShellCommandLineTests
{
    /// <summary>
    /// 放送の番組名は全角空白をよく含む。char.IsWhiteSpace で切ると、そこを含む録画ファイルの
    /// パスが途中で切れ、ffmpeg が「No such file or directory」で落ちる。
    /// </summary>
    [Fact]
    public void AFullWidthSpaceInsideAPathIsNotASeparator()
    {
        string[] parts = ShellCommandLine.Split("/usr/bin/ffmpeg -i /recorded/ねこ物件　＃５.m2ts");

        Assert.Equal(["/usr/bin/ffmpeg", "-i", "/recorded/ねこ物件　＃５.m2ts"], parts);
    }

    [Fact]
    public void AsciiWhitespaceStillSeparatesArguments()
    {
        Assert.Equal(["a", "b", "c", "d"], ShellCommandLine.Split("a b\tc\nd"));
    }

    [Fact]
    public void QuotesKeepSpacesTogether()
    {
        Assert.Equal(["ffmpeg", "-i", "/recorded/a b.m2ts"], ShellCommandLine.Split("ffmpeg -i '/recorded/a b.m2ts'"));
    }
}
