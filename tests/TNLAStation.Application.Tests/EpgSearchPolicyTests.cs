using TNLAStation.Application.Models;
using TNLAStation.Domain;

namespace TNLAStation.Application.Tests;

/// <summary>
/// 検索条件の判定は、番組検索とルール予約の両方が同じ答えを出す前提の土台なので、
/// HTTP を通さず分岐ごとに固定する。
/// </summary>
public sealed class EpgSearchPolicyTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 20, 12, 0, 0, TimeSpan.FromHours(9));

    [Fact]
    public void ValidateRejectsAQueryWithoutAnyCondition()
    {
        InvalidOperationException error =
            Assert.Throws<InvalidOperationException>(() => EpgSearchPolicy.Validate(new EpgSearchQuery()));

        Assert.Equal("InvalidFindRuleOption", error.Message);
    }

    [Fact]
    public void ValidateRejectsAKeywordWithoutAFieldToSearch()
    {
        var query = new EpgSearchQuery(Keyword: "アニメ", Gr: true);

        Assert.Throws<InvalidOperationException>(() => EpgSearchPolicy.Validate(query));
    }

    [Fact]
    public void ValidateAcceptsAKeywordOnceAFieldIsSelected()
    {
        EpgSearchPolicy.Validate(new EpgSearchQuery(Keyword: "アニメ", Name: true));
    }

    [Fact]
    public void ValidateRejectsTimesThatSelectNoWeekday()
    {
        var query = new EpgSearchQuery(Times: [new EpgSearchTime(Week: 0)]);

        Assert.Throws<InvalidOperationException>(() => EpgSearchPolicy.Validate(query));
    }

    [Fact]
    public void ValidateRejectsANegativeLimit()
    {
        var query = new EpgSearchQuery(Keyword: "アニメ", Name: true, Limit: -1);

        Assert.Throws<ArgumentOutOfRangeException>(() => EpgSearchPolicy.Validate(query));
    }

    [Fact]
    public void ProgramsThatAlreadyEndedNeverMatch()
    {
        EpgProgram program = CreateProgram(startAt: Now.AddHours(-3), endAt: Now.AddHours(-2));

        Assert.False(EpgSearchPolicy.Matches(program, new EpgSearchQuery(Keyword: "ニュース", Name: true), Now));
    }

    [Fact]
    public void KeywordMatchingNormalizesWidthAndRequiresEveryTerm()
    {
        EpgProgram program = CreateProgram(halfWidthName: "NHK ニュース7");

        Assert.True(EpgSearchPolicy.Matches(program, new EpgSearchQuery(Keyword: "ＮＨＫ　ニュース", Name: true), Now));
        Assert.False(EpgSearchPolicy.Matches(program, new EpgSearchQuery(Keyword: "NHK 天気", Name: true), Now));
    }

    [Fact]
    public void KeywordMatchingIgnoresCaseUnlessAsked()
    {
        EpgProgram program = CreateProgram(halfWidthName: "Music Station");

        Assert.True(EpgSearchPolicy.Matches(program, new EpgSearchQuery(Keyword: "music", Name: true), Now));
        Assert.False(EpgSearchPolicy.Matches(
            program,
            new EpgSearchQuery(Keyword: "music", Name: true, KeyCaseSensitive: true),
            Now));
    }

    [Fact]
    public void KeywordCanBeARegularExpression()
    {
        EpgProgram program = CreateProgram(halfWidthName: "第12話 決戦");

        Assert.True(EpgSearchPolicy.Matches(
            program,
            new EpgSearchQuery(Keyword: "第[0-9]+話", Name: true, KeyRegularExpression: true),
            Now));
    }

    [Fact]
    public void SearchedFieldsAreLimitedToTheSelectedOnes()
    {
        EpgProgram program = CreateProgram(halfWidthName: "映画", halfWidthDescription: "特撮の傑作");

        Assert.False(EpgSearchPolicy.Matches(program, new EpgSearchQuery(Keyword: "特撮", Name: true), Now));
        Assert.True(EpgSearchPolicy.Matches(program, new EpgSearchQuery(Keyword: "特撮", Description: true), Now));
    }

    [Fact]
    public void IgnoreKeywordRemovesAnOtherwiseMatchingProgram()
    {
        EpgProgram program = CreateProgram(halfWidthName: "アニメ 再放送");

        var query = new EpgSearchQuery(
            Keyword: "アニメ",
            Name: true,
            IgnoreKeyword: "再放送",
            IgnoreName: true);

        Assert.False(EpgSearchPolicy.Matches(program, query, Now));
    }

    [Fact]
    public void ChannelIdsTakePrecedenceOverBroadcastTypes()
    {
        EpgProgram program = CreateProgram(channelId: 100, channelType: "GR");

        Assert.True(EpgSearchPolicy.Matches(program, new EpgSearchQuery(ChannelIds: [100], Bs: true), Now));
        Assert.False(EpgSearchPolicy.Matches(program, new EpgSearchQuery(ChannelIds: [200], Gr: true), Now));
    }

    [Fact]
    public void BroadcastTypesFilterWhenNoChannelIsGiven()
    {
        EpgProgram program = CreateProgram(channelType: "BS");

        Assert.True(EpgSearchPolicy.Matches(program, new EpgSearchQuery(Bs: true), Now));
        Assert.False(EpgSearchPolicy.Matches(program, new EpgSearchQuery(Gr: true), Now));
    }

    [Fact]
    public void GenreMatchesAnyOfTheThreeSlotsAndHonorsSubGenre()
    {
        EpgProgram program = CreateProgram() with { Genre2 = 7, SubGenre2 = 1 };

        Assert.True(EpgSearchPolicy.Matches(program, new EpgSearchQuery(Genres: [new EpgSearchGenre(7)]), Now));
        Assert.True(EpgSearchPolicy.Matches(program, new EpgSearchQuery(Genres: [new EpgSearchGenre(7, 1)]), Now));
        Assert.False(EpgSearchPolicy.Matches(program, new EpgSearchQuery(Genres: [new EpgSearchGenre(7, 2)]), Now));
    }

    [Fact]
    public void TimeRangeMatchesTheWeekdayBitAndTheStartHourWindow()
    {
        // 2026-07-21 は火曜日。曜日は日曜を最下位ビットとするビットマスク。
        EpgProgram program = CreateProgram(
            startAt: new DateTimeOffset(2026, 7, 21, 21, 0, 0, TimeSpan.FromHours(9)),
            endAt: new DateTimeOffset(2026, 7, 21, 22, 0, 0, TimeSpan.FromHours(9)));

        const int tuesday = 1 << 2;
        const int monday = 1 << 1;

        Assert.True(EpgSearchPolicy.Matches(
            program,
            new EpgSearchQuery(Times: [new EpgSearchTime(Week: tuesday, Start: 20, Range: 3)]),
            Now));
        Assert.False(EpgSearchPolicy.Matches(
            program,
            new EpgSearchQuery(Times: [new EpgSearchTime(Week: tuesday, Start: 6, Range: 3)]),
            Now));
        Assert.False(EpgSearchPolicy.Matches(
            program,
            new EpgSearchQuery(Times: [new EpgSearchTime(Week: monday, Start: 20, Range: 3)]),
            Now));
    }

    [Fact]
    public void DurationBoundsAreInSeconds()
    {
        EpgProgram program = CreateProgram(durationMilliseconds: 1_800_000);

        Assert.True(EpgSearchPolicy.Matches(program, new EpgSearchQuery(DurationMin: 1_800), Now));
        Assert.False(EpgSearchPolicy.Matches(program, new EpgSearchQuery(DurationMin: 1_801), Now));
        Assert.True(EpgSearchPolicy.Matches(program, new EpgSearchQuery(DurationMax: 1_800), Now));
        Assert.False(EpgSearchPolicy.Matches(program, new EpgSearchQuery(DurationMax: 1_799), Now));
    }

    [Fact]
    public void FreeOnlyExcludesPaidProgramsButNotTheOtherWayAround()
    {
        EpgProgram paid = CreateProgram() with { IsFree = false };

        Assert.False(EpgSearchPolicy.Matches(paid, new EpgSearchQuery(IsFree: true), Now));
        Assert.True(EpgSearchPolicy.Matches(CreateProgram(), new EpgSearchQuery(IsFree: true), Now));
    }

    [Fact]
    public void SearchPeriodsMatchOnTheProgramStart()
    {
        EpgProgram program = CreateProgram(startAt: Now.AddHours(1), endAt: Now.AddHours(2));

        Assert.True(EpgSearchPolicy.Matches(
            program,
            new EpgSearchQuery(Gr: true, SearchPeriods: [new EpgSearchPeriod(Now, Now.AddHours(3))]),
            Now));
        Assert.False(EpgSearchPolicy.Matches(
            program,
            new EpgSearchQuery(Gr: true, SearchPeriods: [new EpgSearchPeriod(Now.AddHours(4), Now.AddHours(6))]),
            Now));
    }

    private static EpgProgram CreateProgram(
        long channelId = 3_273_601_024,
        string channelType = "GR",
        string halfWidthName = "テスト番組",
        string? halfWidthDescription = null,
        DateTimeOffset? startAt = null,
        DateTimeOffset? endAt = null,
        long durationMilliseconds = 3_600_000)
    {
        DateTimeOffset start = startAt ?? Now.AddHours(1);
        DateTimeOffset end = endAt ?? start.AddHours(1);

        return new EpgProgram(
            Id: 1,
            UpdateTime: Now,
            ChannelId: channelId,
            EventId: 1,
            ServiceId: 1024,
            NetworkId: 32736,
            StartAt: start,
            EndAt: end,
            StartHour: start.Hour,
            Week: (int)start.DayOfWeek,
            DurationMilliseconds: durationMilliseconds,
            IsFree: true,
            Name: halfWidthName,
            HalfWidthName: halfWidthName,
            ShortName: halfWidthName,
            ChannelType: channelType,
            Channel: "27",
            Description: halfWidthDescription,
            HalfWidthDescription: halfWidthDescription);
    }
}
