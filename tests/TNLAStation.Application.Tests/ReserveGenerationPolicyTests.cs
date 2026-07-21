using TNLAStation.Application.Models;
using TNLAStation.Domain;

namespace TNLAStation.Application.Tests;

/// <summary>
/// 予約生成は、チューナーが足りない・同じ番組が二度来る・人が明示的に入れた、といった
/// 実機でしか起きない状況で判断を誤る。番組表もチューナーも持たずに、その分岐を固定する。
/// </summary>
public sealed class ReserveGenerationPolicyTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 20, 12, 0, 0, TimeSpan.FromHours(9));

    [Fact]
    public void AnEnabledRuleReservesTheProgramsItMatches()
    {
        ReserveGenerationInput input = CreateInput(
            rules: [CreateRule(keyword: "ニュース")],
            programs: [CreateProgram(1, name: "夜のニュース"), CreateProgram(2, name: "映画劇場")]);

        IReadOnlyList<ReserveAssignment> result = ReserveGenerationPolicy.Generate(input);

        ReserveAssignment assignment = Assert.Single(result);
        Assert.Equal("夜のニュース", assignment.Target.Name);
        Assert.Equal(1, assignment.Target.RuleId);
        Assert.False(assignment.IsConflict);
    }

    [Fact]
    public void ADisabledRuleReservesNothing()
    {
        RecordingRule rule = CreateRule(keyword: "ニュース");
        ReserveGenerationInput input = CreateInput(
            rules: [rule with { ReserveOption = rule.ReserveOption with { Enable = false } }],
            programs: [CreateProgram(1, name: "夜のニュース")]);

        Assert.Empty(ReserveGenerationPolicy.Generate(input));
    }

    [Fact]
    public void TwoProgramsOnOneChannelShareASingleTuner()
    {
        ReserveGenerationInput input = CreateInput(
            rules: [CreateRule(keyword: "ニュース")],
            programs:
            [
                CreateProgram(1, name: "朝のニュース"),
                CreateProgram(2, name: "昼のニュース", channelId: 100),
            ],
            tuners: [new TunerDevice(0, ["GR"])]);

        IReadOnlyList<ReserveAssignment> result = ReserveGenerationPolicy.Generate(input);

        Assert.Equal(2, result.Count);
        Assert.All(result, assignment => Assert.Equal(0, assignment.TunerIndex));
        Assert.DoesNotContain(result, assignment => assignment.IsConflict);
    }

    [Fact]
    public void AProgramOnAnotherChannelNeedsItsOwnTuner()
    {
        ReserveGenerationInput input = CreateInput(
            rules: [CreateRule(keyword: "ニュース")],
            programs:
            [
                CreateProgram(1, name: "朝のニュース"),
                CreateProgram(2, name: "昼のニュース", channelId: 200),
            ],
            tuners: [new TunerDevice(0, ["GR"]), new TunerDevice(1, ["GR"])]);

        IReadOnlyList<ReserveAssignment> result = ReserveGenerationPolicy.Generate(input);

        Assert.Equal([0, 1], result.Select(assignment => assignment.TunerIndex).ToArray());
    }

    [Fact]
    public void TheProgramThatFindsNoTunerIsMarkedAsAConflict()
    {
        ReserveGenerationInput input = CreateInput(
            rules: [CreateRule(keyword: "ニュース")],
            programs:
            [
                CreateProgram(1, name: "朝のニュース"),
                CreateProgram(2, name: "昼のニュース", channelId: 200),
            ],
            tuners: [new TunerDevice(0, ["GR"])]);

        IReadOnlyList<ReserveAssignment> result = ReserveGenerationPolicy.Generate(input);

        Assert.Equal(0, result[0].TunerIndex);
        Assert.Null(result[1].TunerIndex);
        Assert.True(result[1].IsConflict);
    }

    [Fact]
    public void ATunerThatCannotReceiveTheBandIsNotUsed()
    {
        RecordingRule rule = CreateRule(keyword: "ニュース");
        ReserveGenerationInput input = CreateInput(
            rules: [rule with { SearchOption = rule.SearchOption with { Gr = false, Bs = true } }],
            programs: [CreateProgram(1, name: "衛星ニュース", channelType: "BS")],
            tuners: [new TunerDevice(0, ["GR"])]);

        ReserveAssignment assignment = Assert.Single(ReserveGenerationPolicy.Generate(input));

        Assert.True(assignment.IsConflict);
    }

    [Fact]
    public void AManualReserveKeepsItsTunerWhenARuleWantsTheSameSlot()
    {
        ReserveGenerationInput input = CreateInput(
            rules: [CreateRule(keyword: "ニュース")],
            programs: [CreateProgram(1, name: "朝のニュース", channelId: 200)],
            manualReserves: [CreateManualReserve(id: 10)],
            tuners: [new TunerDevice(0, ["GR"])]);

        IReadOnlyList<ReserveAssignment> result = ReserveGenerationPolicy.Generate(input);

        ReserveAssignment manual = result.Single(assignment => assignment.Target.ManualReserveId == 10);
        ReserveAssignment fromRule = result.Single(assignment => assignment.Target.RuleId == 1);
        Assert.Equal(0, manual.TunerIndex);
        Assert.True(fromRule.IsConflict);
    }

    [Fact]
    public void ARuleDoesNotReserveAProgramThatIsAlreadyReservedByHand()
    {
        ReserveGenerationInput input = CreateInput(
            rules: [CreateRule(keyword: "ニュース")],
            programs: [CreateProgram(1, name: "朝のニュース")],
            manualReserves: [CreateManualReserve(id: 10, programId: 1)]);

        ReserveAssignment assignment = Assert.Single(ReserveGenerationPolicy.Generate(input));

        Assert.Equal(10, assignment.Target.ManualReserveId);
    }

    [Fact]
    public void AvoidDuplicateMarksTheSecondBroadcastOfTheSameProgram()
    {
        RecordingRule rule = CreateRule(keyword: "ニュース");
        ReserveGenerationInput input = CreateInput(
            rules: [rule with { ReserveOption = rule.ReserveOption with { AvoidDuplicate = true } }],
            programs:
            [
                CreateProgram(1, name: "朝のニュース"),
                CreateProgram(2, name: "朝のニュース", startAt: Now.AddHours(5)),
            ]);

        IReadOnlyList<ReserveAssignment> result = ReserveGenerationPolicy.Generate(input);

        Assert.False(result[0].Target.IsOverlap);
        Assert.True(result[1].Target.IsOverlap);
        // 重複で録らないものは競合ではない。チューナーが足りないわけではないため。
        Assert.False(result[1].IsConflict);
    }

    [Fact]
    public void AvoidDuplicateLooksAtWhatWasAlreadyRecorded()
    {
        RecordingRule rule = CreateRule(keyword: "ニュース");
        ReserveGenerationInput input = CreateInput(
            rules: [rule with { ReserveOption = rule.ReserveOption with { AvoidDuplicate = true } }],
            programs: [CreateProgram(1, name: "朝のニュース")],
            history: [new RecordedHistoryItem("朝のニュース", 100, Now.AddDays(-3))]);

        Assert.True(Assert.Single(ReserveGenerationPolicy.Generate(input)).Target.IsOverlap);
    }

    [Fact]
    public void AvoidDuplicateIgnoresRecordingsOlderThanTheConfiguredPeriod()
    {
        RecordingRule rule = CreateRule(keyword: "ニュース");
        ReserveGenerationInput input = CreateInput(
            rules:
            [
                rule with
                {
                    ReserveOption = rule.ReserveOption with { AvoidDuplicate = true, PeriodToAvoidDuplicate = 6 },
                },
            ],
            programs: [CreateProgram(1, name: "朝のニュース")],
            history: [new RecordedHistoryItem("朝のニュース", 100, Now.AddDays(-30))]);

        Assert.False(Assert.Single(ReserveGenerationPolicy.Generate(input)).Target.IsOverlap);
    }

    [Fact]
    public void ASkippedReserveReleasesItsTunerToTheNextProgram()
    {
        ReserveGenerationInput input = CreateInput(
            rules: [CreateRule(keyword: "ニュース")],
            programs:
            [
                CreateProgram(1, name: "朝のニュース"),
                CreateProgram(2, name: "昼のニュース", channelId: 200),
            ],
            tuners: [new TunerDevice(0, ["GR"])],
            skipStates: new Dictionary<string, bool> { ["rule:1:1"] = true });

        IReadOnlyList<ReserveAssignment> result = ReserveGenerationPolicy.Generate(input);

        Assert.True(result[0].Target.IsSkip);
        Assert.Null(result[0].TunerIndex);
        Assert.False(result[0].IsConflict);
        Assert.Equal(0, result[1].TunerIndex);
    }

    [Fact]
    public void TheRuleWithTheHigherPriorityTakesTheTuner()
    {
        ReserveGenerationInput input = CreateInput(
            rules: [CreateRule(keyword: "ニュース", id: 1), CreateRule(keyword: "映画", id: 2, priority: ReservePriority.High)],
            programs:
            [
                CreateProgram(1, name: "朝のニュース"),
                CreateProgram(2, name: "映画劇場", channelId: 200),
            ],
            tuners: [new TunerDevice(0, ["GR"])]);

        IReadOnlyList<ReserveAssignment> result = ReserveGenerationPolicy.Generate(input);

        ReserveAssignment movie = result.Single(assignment => assignment.Target.RuleId == 2);
        ReserveAssignment news = result.Single(assignment => assignment.Target.RuleId == 1);
        Assert.Equal(0, movie.TunerIndex);
        Assert.True(news.IsConflict);
    }

    [Fact]
    public void APrioritisedRuleBeatsAManualReserve()
    {
        ReserveGenerationInput input = CreateInput(
            rules: [CreateRule(keyword: "映画", priority: ReservePriority.High)],
            programs: [CreateProgram(1, name: "映画劇場", channelId: 200)],
            manualReserves: [CreateManualReserve(id: 10)],
            tuners: [new TunerDevice(0, ["GR"])]);

        IReadOnlyList<ReserveAssignment> result = ReserveGenerationPolicy.Generate(input);

        Assert.Equal(0, result.Single(assignment => assignment.Target.RuleId == 1).TunerIndex);
        Assert.True(result.Single(assignment => assignment.Target.ManualReserveId == 10).IsConflict);
    }

    [Fact]
    public void ALoweredManualReserveGivesWayToANormalRule()
    {
        ReserveGenerationInput input = CreateInput(
            rules: [CreateRule(keyword: "ニュース")],
            programs: [CreateProgram(1, name: "朝のニュース", channelId: 200)],
            manualReserves: [CreateManualReserve(id: 10, priority: ReservePriority.Low)],
            tuners: [new TunerDevice(0, ["GR"])]);

        IReadOnlyList<ReserveAssignment> result = ReserveGenerationPolicy.Generate(input);

        Assert.Equal(0, result.Single(assignment => assignment.Target.RuleId == 1).TunerIndex);
        Assert.True(result.Single(assignment => assignment.Target.ManualReserveId == 10).IsConflict);
    }

    [Fact]
    public void ProgramsThatAlreadyEndedAreNotReserved()
    {
        ReserveGenerationInput input = CreateInput(
            rules: [CreateRule(keyword: "ニュース")],
            programs: [CreateProgram(1, name: "朝のニュース", startAt: Now.AddHours(-3))]);

        Assert.Empty(ReserveGenerationPolicy.Generate(input));
    }

    [Fact]
    public void AReserveKeyStaysTheSameAcrossGenerations()
    {
        ReserveGenerationInput input = CreateInput(
            rules: [CreateRule(keyword: "ニュース")],
            programs: [CreateProgram(1, name: "朝のニュース")],
            manualReserves: [CreateManualReserve(id: 10, channelId: 200)]);

        IReadOnlyList<ReserveTarget> first = ReserveGenerationPolicy.CollectTargets(input);
        IReadOnlyList<ReserveTarget> second = ReserveGenerationPolicy.CollectTargets(input);

        Assert.Equal(["manual:10", "rule:1:1"], first.Select(target => target.Key).Order().ToArray());
        Assert.Equal(first.Select(target => target.Key), second.Select(target => target.Key));
    }

    [Fact]
    public void WithoutATunerEveryReserveConflicts()
    {
        ReserveGenerationInput input = CreateInput(
            rules: [CreateRule(keyword: "ニュース")],
            programs: [CreateProgram(1, name: "朝のニュース")],
            tuners: []);

        Assert.True(Assert.Single(ReserveGenerationPolicy.Generate(input)).IsConflict);
    }

    private static ReserveGenerationInput CreateInput(
        IReadOnlyList<RecordingRule>? rules = null,
        IReadOnlyList<EpgProgram>? programs = null,
        IReadOnlyList<ManualReserve>? manualReserves = null,
        IReadOnlyList<TunerDevice>? tuners = null,
        IReadOnlyList<RecordedHistoryItem>? history = null,
        IReadOnlyDictionary<string, bool>? skipStates = null) =>
        new(
            Now,
            rules ?? [],
            programs ?? [],
            manualReserves ?? [],
            tuners ?? [new TunerDevice(0, ["GR"]), new TunerDevice(1, ["GR"])],
            history ?? [],
            skipStates);

    private static RecordingRule CreateRule(
        string keyword,
        long id = 1,
        int priority = ReservePriority.Normal) =>
        new(
            id,
            IsTimeSpecification: false,
            new EpgSearchQuery(Keyword: keyword, Name: true, Gr: true),
            new RuleReserveOption(
                Enable: true,
                AllowEndLack: true,
                AvoidDuplicate: false,
                Priority: priority));

    private static ManualReserve CreateManualReserve(
        long id,
        long? programId = null,
        long channelId = 100,
        int priority = ReservePriority.Normal) =>
        new(
            id,
            channelId,
            "GR",
            Now.AddHours(1),
            Now.AddHours(2),
            "手動で入れた番組",
            programId,
            Priority: priority);

    private static EpgProgram CreateProgram(
        long id,
        string name,
        long channelId = 100,
        string channelType = "GR",
        DateTimeOffset? startAt = null)
    {
        DateTimeOffset start = startAt ?? Now.AddHours(1);
        return new EpgProgram(
            id,
            UpdateTime: Now,
            ChannelId: channelId,
            EventId: id,
            ServiceId: 1024,
            NetworkId: 32736,
            StartAt: start,
            EndAt: start.AddHours(1),
            StartHour: start.Hour,
            Week: (int)start.DayOfWeek,
            DurationMilliseconds: 3_600_000,
            IsFree: true,
            Name: name,
            HalfWidthName: name,
            ShortName: name,
            ChannelType: channelType,
            Channel: "27");
    }
}
