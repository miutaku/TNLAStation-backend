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

public sealed class ThumbnailCommandTests
{
    private const string Default =
        "%FFMPEG% -ss %THUMBNAIL_POSITION% -y -i %INPUT% -vframes 1 -f image2 -s %THUMBNAIL_SIZE% %OUTPUT%";

    /// <summary>
    /// 既定の thumbnailCmd は %INPUT% を引用符で囲まない。先に置換すると、空白を含む
    /// 番組名のパスがそこで別の引数へ割れ、ffmpeg が入力を開けずに落ちる。
    /// </summary>
    [Fact]
    public void APathWithSpacesStaysASingleArgument()
    {
        string[] parts = TNLAStation.FfmpegWorker.Media.ThumbnailRunner.BuildCommand(
            Default,
            "/usr/bin/ffmpeg",
            "/recorded/シリアナ [字]-2026年07月31日.m2ts",
            "/thumbnails/41.jpg",
            10,
            "480x270");

        Assert.Equal("/usr/bin/ffmpeg", parts[0]);
        Assert.Contains("/recorded/シリアナ [字]-2026年07月31日.m2ts", parts);
        Assert.Contains("/thumbnails/41.jpg", parts);
        Assert.Equal(["-ss", "10", "-y", "-i"], parts[1..5]);
    }
}
