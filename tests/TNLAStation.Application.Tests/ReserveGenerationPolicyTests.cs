using System.Globalization;
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
    public void TwoServicesOnTheSamePhysicalChannelShareASingleTuner()
    {
        // 上流の Tuner.add は物理チャンネル (channel) で相乗り可否を決める。ChannelId (サービス)
        // が違っても、同じ物理チャンネルに多重化された別サービスなら 1 本のチューナーで録れる。
        ReserveGenerationInput input = CreateInput(
            rules: [CreateRule(keyword: "ニュース")],
            programs:
            [
                CreateProgram(1, name: "朝のニュース", channelId: 100, channel: "27"),
                CreateProgram(2, name: "昼のニュース", channelId: 101, channel: "27"),
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
    public void ARuleAlsoReservesAProgramThatIsAlreadyReservedByHand()
    {
        // 上流はルール側で手動予約との重複を除外しない。同じ番組を手動とルールの両方が
        // 掴むと、見た目には重複した予約が並ぶ (二重登録を拒否するのは追加時のエラーだけで、
        // ルール生成側までは防がない非対称な仕様)。同じ番組は channel も同じになるので、
        // チューナーは相乗りになり競合にはならない。
        ReserveGenerationInput input = CreateInput(
            rules: [CreateRule(keyword: "ニュース")],
            programs: [CreateProgram(1, name: "朝のニュース")],
            manualReserves: [CreateManualReserve(id: 10, programId: 1)],
            tuners: [new TunerDevice(0, ["GR"])]);

        IReadOnlyList<ReserveAssignment> result = ReserveGenerationPolicy.Generate(input);

        Assert.Equal(2, result.Count);
        Assert.All(result, assignment => Assert.Equal(0, assignment.TunerIndex));
        Assert.DoesNotContain(result, assignment => assignment.IsConflict);
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
    public void AvoidDuplicateDoesNotMatchTheSameNameOnADifferentChannel()
    {
        // 上流は番組名だけでなく放送局も一致する場合だけ重複と見なす。同名の別局再放送を
        // 重複扱いにすると、録るべきものを録り逃す。
        RecordingRule rule = CreateRule(keyword: "ニュース");
        ReserveGenerationInput input = CreateInput(
            rules: [rule with { ReserveOption = rule.ReserveOption with { AvoidDuplicate = true } }],
            programs: [CreateProgram(1, name: "朝のニュース", channelId: 200)],
            history: [new RecordedHistoryItem("朝のニュース", 100, Now.AddDays(-3))]);

        Assert.False(Assert.Single(ReserveGenerationPolicy.Generate(input)).Target.IsOverlap);
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
            states: new ReserveStates(new HashSet<string> { "rule:1:1" }, new HashSet<string>()));

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
    public void AManualReserveBeatsAPrioritisedRule()
    {
        // EPGStation の sortReserve は ruleId を持たない予約 (手動) を必ずルールより先に置く。
        // TNLAStation 独自の priority はこの大小関係の後にしか効かない — ルールの priority を
        // 上げても、手動予約からチューナーを奪えてはいけない。
        ReserveGenerationInput input = CreateInput(
            rules: [CreateRule(keyword: "映画", priority: ReservePriority.High)],
            programs: [CreateProgram(1, name: "映画劇場", channelId: 200)],
            manualReserves: [CreateManualReserve(id: 10)],
            tuners: [new TunerDevice(0, ["GR"])]);

        IReadOnlyList<ReserveAssignment> result = ReserveGenerationPolicy.Generate(input);

        Assert.Equal(0, result.Single(assignment => assignment.Target.ManualReserveId == 10).TunerIndex);
        Assert.True(result.Single(assignment => assignment.Target.RuleId == 1).IsConflict);
    }

    [Fact]
    public void ALoweredManualReserveStillBeatsANormalRule()
    {
        // priority を下げても、手動予約であることそのものの優先度 (EPGStation 互換) は覆らない。
        ReserveGenerationInput input = CreateInput(
            rules: [CreateRule(keyword: "ニュース")],
            programs: [CreateProgram(1, name: "朝のニュース", channelId: 200)],
            manualReserves: [CreateManualReserve(id: 10, priority: ReservePriority.Low)],
            tuners: [new TunerDevice(0, ["GR"])]);

        IReadOnlyList<ReserveAssignment> result = ReserveGenerationPolicy.Generate(input);

        Assert.Equal(0, result.Single(assignment => assignment.Target.ManualReserveId == 10).TunerIndex);
        Assert.True(result.Single(assignment => assignment.Target.RuleId == 1).IsConflict);
    }

    [Fact]
    public void AmongTwoRulesTheEarlierCreatedRuleWinsTheTuner()
    {
        // EPGStation の sortReserve はルールどうしでは ruleId の小さい方 (先に作られた方) を先に置く。
        ReserveGenerationInput input = CreateInput(
            rules:
            [
                CreateRule(id: 1, keyword: "アニメ"),
                CreateRule(id: 2, keyword: "映画"),
            ],
            programs:
            [
                CreateProgram(1, name: "アニメタイム", channelId: 200, startAt: Now.AddHours(1)),
                CreateProgram(2, name: "映画劇場", channelId: 201, startAt: Now.AddHours(1)),
            ],
            tuners: [new TunerDevice(0, ["GR"])]);

        IReadOnlyList<ReserveAssignment> result = ReserveGenerationPolicy.Generate(input);

        Assert.Equal(0, result.Single(assignment => assignment.Target.RuleId == 1).TunerIndex);
        Assert.True(result.Single(assignment => assignment.Target.RuleId == 2).IsConflict);
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
    public void AManualProgramReserveFollowsTheLatestEpgTime()
    {
        EpgProgram moved = CreateProgram(
            42,
            name: "延長後の番組",
            channelId: 200,
            startAt: Now.AddHours(2));
        ReserveGenerationInput input = CreateInput(
            programs: [moved],
            manualReserves: [CreateManualReserve(id: 10, programId: 42)]);

        ReserveTarget target = Assert.Single(ReserveGenerationPolicy.CollectTargets(input));

        Assert.Equal(moved.StartAt, target.StartAt);
        Assert.Equal(moved.EndAt, target.EndAt);
        Assert.Equal(moved.ChannelId, target.ChannelId);
        Assert.Equal(moved.Name, target.Name);
    }

    [Fact]
    public void ATimeSpecifiedReserveDoesNotFollowEpg()
    {
        ManualReserve specified = CreateManualReserve(id: 10, programId: 42) with
        {
            IsTimeSpecified = true,
        };
        ReserveGenerationInput input = CreateInput(
            programs: [CreateProgram(42, name: "移動した番組", startAt: Now.AddHours(3))],
            manualReserves: [specified]);

        ReserveTarget target = Assert.Single(ReserveGenerationPolicy.CollectTargets(input));

        Assert.Equal(specified.StartAt, target.StartAt);
        Assert.Equal(specified.EndAt, target.EndAt);
        Assert.Equal(specified.Name, target.Name);
    }

    [Fact]
    public void AnEventRelayKeepsTheRootKeyAndSwitchesToTheActiveService()
    {
        EpgProgram relay = CreateProgram(
            2,
            name: "中継",
            channelId: 101,
            startAt: Now.AddMinutes(-5));
        EpgProgram root = CreateProgram(
            1,
            name: "中継",
            startAt: Now.AddHours(-1)) with
        {
            RelayProgramIds = [relay.Id],
        };
        ReserveGenerationInput input = CreateInput(
            rules: [CreateRule(keyword: "中継")],
            programs: [root, relay]);

        ReserveTarget target = Assert.Single(ReserveGenerationPolicy.CollectTargets(input));

        Assert.Equal("rule:1:1", target.Key);
        Assert.Equal(101, target.ChannelId);
        Assert.Equal(root.StartAt, target.StartAt);
        Assert.Equal(relay.EndAt, target.EndAt);
        Assert.Equal(root.Name, target.Name);
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
        ReserveStates? states = null) =>
        new(
            Now,
            rules ?? [],
            programs ?? [],
            manualReserves ?? [],
            tuners ?? [new TunerDevice(0, ["GR"]), new TunerDevice(1, ["GR"])],
            history ?? [],
            states);

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
        int priority = ReservePriority.Normal,
        string? channel = null) =>
        new(
            id,
            channelId,
            "GR",
            Now.AddHours(1),
            Now.AddHours(2),
            "手動で入れた番組",
            programId,
            Priority: priority,
            // 既定では channelId ごとに別の物理チャンネルとみなす。同じ物理チャンネルへの
            // 相乗りを試したいときだけ、呼び出し側で同じ channel を明示する。
            Channel: channel ?? channelId.ToString(CultureInfo.InvariantCulture));

    private static EpgProgram CreateProgram(
        long id,
        string name,
        long channelId = 100,
        string channelType = "GR",
        DateTimeOffset? startAt = null,
        string? channel = null)
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
            Channel: channel ?? channelId.ToString(CultureInfo.InvariantCulture));
    }
}
