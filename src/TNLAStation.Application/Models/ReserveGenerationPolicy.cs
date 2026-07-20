using TNLAStation.Domain;

namespace TNLAStation.Application.Models;

/// <summary>
/// 予約を組み立てる。番組表とルールから録るものを決め、チューナーを割り当て、
/// 録れないものに理由を付ける。副作用を持たないので、番組表もチューナーも無しに検証できる。
/// </summary>
public static class ReserveGenerationPolicy
{
    public static IReadOnlyList<ReserveAssignment> Generate(ReserveGenerationInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        return Assign(CollectTargets(input), input.Tuners);
    }

    /// <summary>
    /// 録る候補を集める。手動予約が先で、ルールは同じ番組を二重に録らない。
    /// </summary>
    public static IReadOnlyList<ReserveTarget> CollectTargets(ReserveGenerationInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var targets = new List<ReserveTarget>();
        foreach (ManualReserve reserve in input.ManualReserves)
        {
            targets.Add(new ReserveTarget(
                reserve.IsTimeSpecified ? ReserveSource.TimeSpecified : ReserveSource.Manual,
                reserve.ChannelId,
                reserve.ChannelType,
                reserve.StartAt,
                reserve.EndAt,
                reserve.Name,
                reserve.ProgramId,
                ManualReserveId: reserve.Id,
                IsSkip: reserve.IsSkip));
        }

        // 手動で入れた番組はルールの対象から外す。同じ番組に 2 つ予約が立つと、
        // 見た目が重複するだけでなくチューナーも二重に取ってしまう。
        var manualProgramIds = input.ManualReserves
            .Where(reserve => reserve.ProgramId is not null)
            .Select(reserve => reserve.ProgramId!.Value)
            .ToHashSet();

        foreach (RecordingRule rule in input.Rules.Where(rule => rule.ReserveOption.Enable))
        {
            targets.AddRange(CollectRuleTargets(rule, input, manualProgramIds));
        }

        return input.SkipStates is null or { Count: 0 }
            ? targets
            : [.. targets.Select(target => input.SkipStates.TryGetValue(target.Key, out bool isSkip) && isSkip
                ? target with { IsSkip = true }
                : target)];
    }

    /// <summary>
    /// チューナーを割り当てる。同じ放送局を同時に録るなら 1 本で足りるので、既にその局へ
    /// 合わせているチューナーを先に使い、空いている本数を無駄に減らさない。
    /// </summary>
    public static IReadOnlyList<ReserveAssignment> Assign(
        IReadOnlyList<ReserveTarget> targets,
        IReadOnlyList<TunerDevice> tuners)
    {
        ArgumentNullException.ThrowIfNull(targets);
        ArgumentNullException.ThrowIfNull(tuners);

        var occupied = new Dictionary<int, List<ReserveTarget>>();
        var result = new List<ReserveAssignment>(targets.Count);

        foreach (ReserveTarget target in targets.OrderBy(Priority).ThenBy(target => target.StartAt)
            .ThenByDescending(target => target.EndAt - target.StartAt)
            .ThenBy(target => target.ChannelId))
        {
            if (target.IsSkip || target.IsOverlap)
            {
                // 録らないと決まっているものはチューナーを使わない。
                result.Add(new ReserveAssignment(target, TunerIndex: null));
                continue;
            }

            int? assigned = FindTuner(target, tuners, occupied);
            if (assigned is { } index)
            {
                if (!occupied.TryGetValue(index, out List<ReserveTarget>? assignments))
                {
                    assignments = [];
                    occupied[index] = assignments;
                }

                assignments.Add(target);
            }

            result.Add(new ReserveAssignment(target, assigned));
        }

        return result;
    }

    private static IEnumerable<ReserveTarget> CollectRuleTargets(
        RecordingRule rule,
        ReserveGenerationInput input,
        HashSet<long> manualProgramIds)
    {
        // 同じルールが同じ番組名を何度も拾うことは珍しくない (再放送、帯番組)。この生成の
        // 中で既に採った名前も重複の判断に入れないと、1 回の生成で重複が残ってしまう。
        var seenNames = new HashSet<string>(StringComparer.Ordinal);

        foreach (EpgProgram program in input.Programs
            .Where(program => !manualProgramIds.Contains(program.Id))
            .Where(program => EpgSearchPolicy.Matches(program, rule.SearchOption, input.Now))
            .OrderBy(program => program.StartAt)
            .ThenBy(program => program.Id))
        {
            bool isOverlap = rule.ReserveOption.AvoidDuplicate &&
                (!seenNames.Add(program.ShortName) || WasRecorded(program, rule, input));

            yield return new ReserveTarget(
                ReserveSource.Rule,
                program.ChannelId,
                program.ChannelType,
                program.StartAt,
                program.EndAt,
                program.Name,
                program.Id,
                RuleId: rule.Id,
                IsOverlap: isOverlap);
        }
    }

    private static bool WasRecorded(EpgProgram program, RecordingRule rule, ReserveGenerationInput input)
    {
        // 期間の指定が無ければ、過去の録画すべてが判断の対象になる。
        DateTimeOffset threshold = rule.ReserveOption.PeriodToAvoidDuplicate is { } days
            ? input.Now.AddDays(-days)
            : DateTimeOffset.MinValue;

        return input.History.Any(item =>
            string.Equals(item.Name, program.ShortName, StringComparison.Ordinal) &&
            item.EndAt >= threshold);
    }

    private static int? FindTuner(
        ReserveTarget target,
        IReadOnlyList<TunerDevice> tuners,
        Dictionary<int, List<ReserveTarget>> occupied)
    {
        TunerDevice[] capable = [.. tuners.Where(tuner =>
            tuner.ChannelTypes.Contains(target.ChannelType, StringComparer.Ordinal))];

        // 既に同じ局へ合わせているチューナーがあれば相乗りする。
        foreach (TunerDevice tuner in capable)
        {
            if (occupied.TryGetValue(tuner.Index, out List<ReserveTarget>? assignments) &&
                assignments.Any(other => Overlaps(other, target) && other.ChannelId == target.ChannelId))
            {
                return tuner.Index;
            }
        }

        foreach (TunerDevice tuner in capable)
        {
            if (!occupied.TryGetValue(tuner.Index, out List<ReserveTarget>? assignments) ||
                assignments.All(other => !Overlaps(other, target) || other.ChannelId == target.ChannelId))
            {
                return tuner.Index;
            }
        }

        return null;
    }

    private static bool Overlaps(ReserveTarget left, ReserveTarget right) =>
        left.StartAt < right.EndAt && right.StartAt < left.EndAt;

    /// <summary>
    /// 人が入れた予約を先に置く。チューナーが足りないとき、後から来たルールに押し出されない。
    /// </summary>
    private static int Priority(ReserveTarget target) => target.Source == ReserveSource.Rule ? 1 : 0;
}
