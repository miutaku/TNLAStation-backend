using TNLAStation.Application.Models;

namespace TNLAStation.Application.Tests;

/// <summary>
/// ルールの追加・更新時に上流 (ReserveOptionChecker.checkRuleOption) がかけている検査を、
/// HTTP を通さず分岐ごとに固定する。<see cref="EpgSearchPolicy.Validate"/> (検索そのものの
/// 空条件チェック) とは別物 — こちらはルールとして保存できる形かどうかを見る。
/// </summary>
public sealed class RuleValidationPolicyTests
{
    private static readonly RuleReserveOption DefaultReserveOption = new(Enable: true, AllowEndLack: true, AvoidDuplicate: false);

    [Fact]
    public void ATimeSpecifiedRuleNeedsKeywordChannelsAndTimes()
    {
        var rule = new RecordingRule(1, IsTimeSpecification: true, new EpgSearchQuery(), DefaultReserveOption);

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(
            () => RuleValidationPolicy.Validate(rule, [], hasEncodeConfig: false, "AddRuleError"));
        Assert.Equal("AddRuleError", error.Message);
    }

    [Fact]
    public void ATimeSpecifiedRuleRejectsAZeroRange()
    {
        var rule = new RecordingRule(
            1,
            IsTimeSpecification: true,
            new EpgSearchQuery(Keyword: "ニュース", ChannelIds: [1], Times: [new EpgSearchTime(1, 0, 0)]),
            DefaultReserveOption);

        Assert.Throws<InvalidOperationException>(
            () => RuleValidationPolicy.Validate(rule, [], hasEncodeConfig: false, "AddRuleError"));
    }

    [Fact]
    public void ATimeSpecifiedRuleWithValidTimesPasses()
    {
        var rule = new RecordingRule(
            1,
            IsTimeSpecification: true,
            new EpgSearchQuery(Keyword: "ニュース", ChannelIds: [1], Times: [new EpgSearchTime(1, 0, 1)]),
            DefaultReserveOption);

        RuleValidationPolicy.Validate(rule, [], hasEncodeConfig: false, "AddRuleError");
    }

    [Fact]
    public void AKeywordWithoutAnySearchFieldIsRejected()
    {
        var rule = new RecordingRule(
            1,
            IsTimeSpecification: false,
            new EpgSearchQuery(Keyword: "アニメ", Gr: true),
            DefaultReserveOption);

        Assert.Throws<InvalidOperationException>(
            () => RuleValidationPolicy.Validate(rule, [], hasEncodeConfig: false, "AddRuleError"));
    }

    [Fact]
    public void KeywordFlagsSetWithoutAKeywordAreRejected()
    {
        // 上流は keyword が無いのに cs/regExp/name/description/extended が立っていると弾く。
        var rule = new RecordingRule(
            1,
            IsTimeSpecification: false,
            new EpgSearchQuery(Name: true, Gr: true),
            DefaultReserveOption);

        Assert.Throws<InvalidOperationException>(
            () => RuleValidationPolicy.Validate(rule, [], hasEncodeConfig: false, "AddRuleError"));
    }

    [Fact]
    public void ChannelIdsCannotBeCombinedWithTheBroadcastTypeFlags()
    {
        var rule = new RecordingRule(
            1,
            IsTimeSpecification: false,
            new EpgSearchQuery(Keyword: "ニュース", Name: true, ChannelIds: [1], Bs: true),
            DefaultReserveOption);

        Assert.Throws<InvalidOperationException>(
            () => RuleValidationPolicy.Validate(rule, [], hasEncodeConfig: false, "AddRuleError"));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0x10)]
    public void AGenreCodeOutsideTheValidRangeIsRejected(int genre)
    {
        var rule = new RecordingRule(
            1,
            IsTimeSpecification: false,
            new EpgSearchQuery(Keyword: "ニュース", Name: true, Genres: [new EpgSearchGenre(genre)]),
            DefaultReserveOption);

        Assert.Throws<InvalidOperationException>(
            () => RuleValidationPolicy.Validate(rule, [], hasEncodeConfig: false, "AddRuleError"));
    }

    [Fact]
    public void ATimeWithoutAnyWeekdayIsRejected()
    {
        var rule = new RecordingRule(
            1,
            IsTimeSpecification: false,
            new EpgSearchQuery(Keyword: "ニュース", Name: true, Times: [new EpgSearchTime(0, 0, 1)]),
            DefaultReserveOption);

        Assert.Throws<InvalidOperationException>(
            () => RuleValidationPolicy.Validate(rule, [], hasEncodeConfig: false, "AddRuleError"));
    }

    [Fact]
    public void ATimeRangeOutsideOneToTwentyThreeHoursIsRejected()
    {
        var rule = new RecordingRule(
            1,
            IsTimeSpecification: false,
            new EpgSearchQuery(Keyword: "ニュース", Name: true, Times: [new EpgSearchTime(1, 0, 24)]),
            DefaultReserveOption);

        Assert.Throws<InvalidOperationException>(
            () => RuleValidationPolicy.Validate(rule, [], hasEncodeConfig: false, "AddRuleError"));
    }

    [Fact]
    public void DurationMinGreaterThanDurationMaxIsRejected()
    {
        var rule = new RecordingRule(
            1,
            IsTimeSpecification: false,
            new EpgSearchQuery(Keyword: "ニュース", Name: true, DurationMin: 7200, DurationMax: 1800),
            DefaultReserveOption);

        Assert.Throws<InvalidOperationException>(
            () => RuleValidationPolicy.Validate(rule, [], hasEncodeConfig: false, "AddRuleError"));
    }

    [Fact]
    public void PeriodToAvoidDuplicateWithoutAvoidDuplicateIsRejected()
    {
        var rule = new RecordingRule(
            1,
            IsTimeSpecification: false,
            new EpgSearchQuery(Keyword: "ニュース", Name: true),
            DefaultReserveOption with { PeriodToAvoidDuplicate = 6, AvoidDuplicate = false });

        Assert.Throws<InvalidOperationException>(
            () => RuleValidationPolicy.Validate(rule, [], hasEncodeConfig: false, "AddRuleError"));
    }

    [Fact]
    public void AnEncodeModeThatIsNotConfiguredIsRejected()
    {
        var rule = new RecordingRule(
            1,
            IsTimeSpecification: false,
            new EpgSearchQuery(Keyword: "ニュース", Name: true),
            DefaultReserveOption,
            EncodeOption: new ReserveEncodeSettings("H.264", null, null, null, null, null, null, null, null, false));

        Assert.Throws<InvalidOperationException>(
            () => RuleValidationPolicy.Validate(rule, [], hasEncodeConfig: true, "AddRuleError"));
    }

    [Fact]
    public void AnEncodeDirectoryWithoutItsModeIsRejected()
    {
        var rule = new RecordingRule(
            1,
            IsTimeSpecification: false,
            new EpgSearchQuery(Keyword: "ニュース", Name: true),
            DefaultReserveOption,
            EncodeOption: new ReserveEncodeSettings(null, null, "anime", null, null, null, null, null, null, false));

        Assert.Throws<InvalidOperationException>(
            () => RuleValidationPolicy.Validate(rule, ["H.264"], hasEncodeConfig: true, "AddRuleError"));
    }

    [Fact]
    public void AConfiguredEncodeModePasses()
    {
        var rule = new RecordingRule(
            1,
            IsTimeSpecification: false,
            new EpgSearchQuery(Keyword: "ニュース", Name: true),
            DefaultReserveOption,
            EncodeOption: new ReserveEncodeSettings("H.264", null, "anime", null, null, null, null, null, null, false));

        RuleValidationPolicy.Validate(rule, ["H.264"], hasEncodeConfig: true, "AddRuleError");
    }

    [Fact]
    public void AWellFormedRulePasses()
    {
        var rule = new RecordingRule(
            1,
            IsTimeSpecification: false,
            new EpgSearchQuery(Keyword: "ニュース", Name: true, Gr: true),
            DefaultReserveOption);

        RuleValidationPolicy.Validate(rule, [], hasEncodeConfig: false, "AddRuleError");
    }
}
