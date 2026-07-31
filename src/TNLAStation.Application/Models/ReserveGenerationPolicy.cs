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
    /// 録る候補を集める。EPGStation はルール側で手動予約との重複を除外しない — 同じ番組を手動と
    /// ルールの両方が掴むと、見た目には重複した予約が並ぶ (二重登録の拒否は追加時の
    /// <c>ReservationManageModelReservedError</c> 側だけの片方向)。同じ番組なら物理チャンネルも
    /// 同じなので、チューナーは相乗りになり本数を無駄には使わない。
    /// </summary>
    public static IReadOnlyList<ReserveTarget> CollectTargets(ReserveGenerationInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var targets = new List<ReserveTarget>();
        Dictionary<long, EpgProgram> programsById = input.Programs.ToDictionary(program => program.Id);
        foreach (ManualReserve reserve in input.ManualReserves)
        {
            // 番組を選んで作った手動予約は、作成時に保存した時刻ではなく最新の EPG を使う。
            // 時刻指定予約は番組表に左右されないという利用者の指定なので、そのままにする。
            EpgProgram? program = !reserve.IsTimeSpecified &&
                reserve.ProgramId is { } programId &&
                programsById.TryGetValue(programId, out EpgProgram? found)
                    ? found
                    : null;
            targets.Add(new ReserveTarget(
                reserve.IsTimeSpecified ? ReserveSource.TimeSpecified : ReserveSource.Manual,
                program?.ChannelId ?? reserve.ChannelId,
                program?.ChannelType ?? reserve.ChannelType,
                program?.StartAt ?? reserve.StartAt,
                program?.EndAt ?? reserve.EndAt,
                program?.Name ?? reserve.Name,
                reserve.ProgramId,
                ManualReserveId: reserve.Id,
                IsSkip: reserve.IsSkip,
                Priority: reserve.Priority,
                Channel: program?.Channel ?? reserve.Channel));
        }

        foreach (RecordingRule rule in input.Rules.Where(rule => rule.ReserveOption.Enable))
        {
            targets.AddRange(CollectRuleTargets(rule, input));
        }

        targets = FollowEventRelays(targets, input.Programs, input.Now);
        ReserveStates states = input.States ?? ReserveStates.Empty;
        return [.. targets.Select(target => target with
        {
            IsSkip = target.IsSkip || states.Skipped.Contains(target.Key),
            // 重複の判断を人が覆していれば、判断し直さずそのまま録る。
            IsOverlap = target.IsOverlap && !states.OverlapCleared.Contains(target.Key),
        })];
    }

    private static List<ReserveTarget> FollowEventRelays(
        IReadOnlyList<ReserveTarget> targets,
        IReadOnlyList<EpgProgram> programs,
        DateTimeOffset now)
    {
        Dictionary<long, EpgProgram> byId = programs.ToDictionary(program => program.Id);
        var parentByChild = new Dictionary<long, long>();
        foreach (EpgProgram program in programs)
        {
            foreach (long childId in program.RelayProgramIds ?? [])
            {
                parentByChild.TryAdd(childId, program.Id);
            }
        }

        var followed = new Dictionary<string, ReserveTarget>(StringComparer.Ordinal);
        foreach (ReserveTarget target in targets)
        {
            if (target.Source == ReserveSource.TimeSpecified ||
                target.ProgramId is not { } targetProgramId)
            {
                followed[target.Key] = target;
                continue;
            }

            long rootId = targetProgramId;
            var visited = new HashSet<long>();
            while (parentByChild.TryGetValue(rootId, out long parentId) && visited.Add(rootId))
            {
                rootId = parentId;
            }

            var chain = new List<EpgProgram>();
            long currentId = rootId;
            visited.Clear();
            while (byId.TryGetValue(currentId, out EpgProgram? current) && visited.Add(currentId))
            {
                chain.Add(current);
                long? nextId = current.RelayProgramIds?.FirstOrDefault(id => byId.ContainsKey(id));
                if (nextId is null or 0)
                {
                    break;
                }

                currentId = nextId.Value;
            }

            if (chain.Count == 0)
            {
                followed[target.Key] = target;
                continue;
            }

            EpgProgram root = chain[0];
            EpgProgram active = chain.LastOrDefault(program => program.StartAt <= now) ?? root;
            EpgProgram terminal = chain[^1];
            ReserveTarget updated = target with
            {
                ProgramId = rootId,
                ChannelId = active.ChannelId,
                ChannelType = active.ChannelType,
                Channel = active.Channel,
                StartAt = root.StartAt,
                EndAt = terminal.EndAt,
                Name = root.Name,
            };
            followed[updated.Key] = updated;
        }

        return [.. followed.Values];
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

        foreach (ReserveTarget target in targets.OrderBy(target => target, AssignmentOrder))
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
        ReserveGenerationInput input)
    {
        // 同じルールが同じ番組名を何度も拾うことは珍しくない (再放送、帯番組)。この生成の
        // 中で既に採った名前も重複の判断に入れないと、1 回の生成で重複が残ってしまう。
        var seenNames = new HashSet<string>(StringComparer.Ordinal);

        foreach (EpgProgram program in input.Programs
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
                IsOverlap: isOverlap,
                Priority: rule.ReserveOption.Priority,
                Channel: program.Channel);
        }
    }

    private static bool WasRecorded(EpgProgram program, RecordingRule rule, ReserveGenerationInput input)
    {
        // 期間の指定が無ければ、過去の録画すべてが判断の対象になる。
        DateTimeOffset threshold = rule.ReserveOption.PeriodToAvoidDuplicate is { } days
            ? input.Now.AddDays(-days)
            : DateTimeOffset.MinValue;

        // EPGStation は番組名だけでなく放送局も一致する場合だけ重複と見なす。同名の別チャンネル
        // 再放送 (同時ネットの別局など) まで重複扱いにすると、録るべきものを録り逃す。
        return input.History.Any(item =>
            string.Equals(item.Name, program.ShortName, StringComparison.Ordinal) &&
            item.ChannelId == program.ChannelId &&
            item.EndAt >= threshold);
    }

    /// <summary>
    /// EPGStation の Tuner.add と同じ判断: チューナーを番号の若い順に見て、対応種別かつ
    /// (今この時間帯に何も入っていない OR 既に入っている最優先の予約と同じ物理チャンネル)
    /// の最初の 1 本を使う。若い番号に空きがあれば、たとえ別の番号で相乗りできても
    /// そちらへは回さない — EPGStation も相乗りを積極的に探しには行かず、素直に空きから使う。
    /// </summary>
    private static int? FindTuner(
        ReserveTarget target,
        IReadOnlyList<TunerDevice> tuners,
        Dictionary<int, List<ReserveTarget>> occupied)
    {
        foreach (TunerDevice tuner in tuners)
        {
            if (!tuner.ChannelTypes.Contains(target.ChannelType, StringComparer.Ordinal))
            {
                continue;
            }

            ReserveTarget? current = occupied.TryGetValue(tuner.Index, out List<ReserveTarget>? assignments)
                ? assignments.FirstOrDefault(other => Overlaps(other, target))
                : null;

            if (current is null || IsSameChannel(current, target))
            {
                return tuner.Index;
            }
        }

        return null;
    }

    /// <summary>
    /// 物理チャンネルが分かっていればそれで比べる (相乗り可能かの本来の基準)。空文字列
    /// (未解決) のときだけ、後方互換として ChannelId で比べる。
    /// </summary>
    private static bool IsSameChannel(ReserveTarget left, ReserveTarget right) =>
        left.Channel.Length > 0 && right.Channel.Length > 0
            ? string.Equals(left.Channel, right.Channel, StringComparison.Ordinal)
            : left.ChannelId == right.ChannelId;

    private static bool Overlaps(ReserveTarget left, ReserveTarget right) =>
        left.StartAt < right.EndAt && right.StartAt < left.EndAt;

    /// <summary>
    /// チューナーが足りないとき、どれを優先してチューナーへ割り当てるか。EPGStation の
    /// sortReserve と同じ基準で並べる:
    /// 1. 手動予約 (ruleId を持たない — 時刻指定を含む) は必ずルール予約より先。
    /// 2. <see cref="ReserveTarget.Priority"/> は EPGStation に無い TNLAStation 独自の項目。
    ///    EPGStation 互換のクライアントは既定値 (Normal=0) のまま送ってくるので、両方 0 のときは
    ///    何も変えない — 明示的に差を付けたときだけ、この段階で効く。
    /// 3. 手動予約どうしは、時刻指定を先に、そのうえで古い方 (ManualReserveId が小さい方) を先に。
    ///    EPGStation は作成/更新時刻で比べるが、こちらは安定した Id で代える。
    /// 4. ルールどうしは ruleId が小さい方 (先に作られた方) を先に。
    /// </summary>
    private static readonly IComparer<ReserveTarget> AssignmentOrder = Comparer<ReserveTarget>.Create((a, b) =>
    {
        bool aIsRule = a.Source == ReserveSource.Rule;
        bool bIsRule = b.Source == ReserveSource.Rule;
        if (aIsRule != bIsRule)
        {
            return aIsRule ? 1 : -1;
        }

        if (a.Priority != b.Priority)
        {
            return b.Priority - a.Priority;
        }

        if (!aIsRule)
        {
            bool aTimeSpecified = a.Source == ReserveSource.TimeSpecified;
            bool bTimeSpecified = b.Source == ReserveSource.TimeSpecified;
            if (aTimeSpecified != bTimeSpecified)
            {
                return aTimeSpecified ? -1 : 1;
            }

            return (a.ManualReserveId ?? 0).CompareTo(b.ManualReserveId ?? 0);
        }

        return (a.RuleId ?? 0).CompareTo(b.RuleId ?? 0);
    });
}
