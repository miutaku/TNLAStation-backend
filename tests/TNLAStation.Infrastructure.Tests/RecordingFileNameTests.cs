using TNLAStation.Domain;
using TNLAStation.Infrastructure.Recording;

namespace TNLAStation.Infrastructure.Tests;

public sealed class RecordingFileNameTests
{
    private static readonly EpgChannel Channel = new(
        Id: 1,
        ServiceId: 1024,
        NetworkId: 32736,
        Name: "テスト放送",
        HalfWidthName: "ﾃｽﾄﾎｳｿｳ",
        RemoteControlKeyId: 1,
        HasLogoData: false,
        ChannelTypeId: 0,
        ChannelType: "GR",
        Channel: "27",
        ServiceType: 1);

    private static readonly Reservation Reserve = new(
        Id: 42,
        IsSkip: false,
        IsConflict: false,
        IsOverlap: false,
        AllowEndLack: true,
        IsTimeSpecified: false,
        IsDeleteOriginalAfterEncode: false,
        ChannelId: 1,
        StartAt: 0,
        EndAt: 0,
        Name: "テスト番組",
        HalfWidthName: "ﾃｽﾄﾊﾞﾝｸﾞﾐ",
        ProgramId: 999);

    private static readonly DateTimeOffset StartAt = new(2026, 3, 4, 21, 5, 9, TimeSpan.Zero);

    [Fact]
    public void AllPlaceholdersAreSubstituted()
    {
        string filename = RecordingFileName.Create(
            "%YEAR%%MONTH%%DAY%-%HOUR%%MIN%%SEC%-%TYPE%-%CHID%-%CHNAME%-%HALF_WIDTH_CHNAME%-%CH%-%SID%-%ID%-%TITLE%-%HALF_WIDTH_TITLE%",
            ".ts",
            StartAt,
            Channel,
            Reserve);

        // 日本時間 (UTC+9) に直すので、UTC 21:05:09 は翌日 06:05:09 になる。
        Assert.Equal(
            "20260305-060509-GR-1-テスト放送-ﾃｽﾄﾎｳｿｳ-27-1024-42-テスト番組-ﾃｽﾄﾊﾞﾝｸﾞﾐ.ts",
            filename);
    }

    [Fact]
    public void DowIsTheJapaneseWeekdayKanjiLikeEpgStation()
    {
        // 日本時間で 2026-03-05 は木曜日。EPGStation は %DOW% を漢字 1 文字 (日〜土) に置換する
        // ので、そのまま数字にしてしまわないことを確かめる。
        string filename = RecordingFileName.Create("%DOW%", ".ts", StartAt, Channel, Reserve);

        Assert.Equal("木.ts", filename);
    }

    [Fact]
    public void MissingChannelFallsBackToPlaceholderDefaults()
    {
        string filename = RecordingFileName.Create(
            "%CHNAME%-%HALF_WIDTH_CHNAME%-%TYPE%-%CH%-%SID%",
            ".ts",
            StartAt,
            channel: null,
            Reserve);

        Assert.Equal("CH-CH-NULL-NULL-NULL.ts", filename);
    }

    [Fact]
    public void IllegalFileSystemCharactersAreSanitized()
    {
        var reserve = Reserve with { Name = "特番: 1/2 決勝?" };

        string filename = RecordingFileName.Create("%TITLE%", ".ts", StartAt, Channel, reserve);

        Assert.Equal("特番_ 1_2 決勝_.ts", filename);
    }

    [Fact]
    public void LongNamesAreTruncatedButKeepTheExtension()
    {
        var reserve = Reserve with { Name = new string('あ', 300) };

        string filename = RecordingFileName.Create("%TITLE%", ".ts", StartAt, Channel, reserve);

        Assert.Equal(200 + ".ts".Length, filename.Length);
        Assert.EndsWith(".ts", filename, StringComparison.Ordinal);
    }
}
