using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TNLAStation.Application.Abstractions;
using TNLAStation.Application.Models;
using TNLAStation.Domain;
using TNLAStation.Infrastructure.Configuration;
using TNLAStation.Infrastructure.Mirakurun;

namespace TNLAStation.Infrastructure.Reserves;

/// <summary>
/// 予約を作り直す。番組表もルールも変わるので、差分を積むより毎回作り直すほうが確かで、
/// 番組が繰り上がったり消えたりしても取り残しが出ない。
/// </summary>
public sealed partial class ReserveGenerator(
    IEpgRepository epg,
    IRuleRepository rules,
    IRecordedHistoryStore history,
    IReserveStore store,
    IMirakurunClient mirakurun,
    ICommandHookRunner hooks,
    IOptions<ReserveOptions> options,
    IOptions<CommandHookOptions> commandHookOptions,
    IRecordingScheduleTrigger recordingSchedule,
    IClientNotifier notifier,
    TimeProvider timeProvider,
    ILogger<ReserveGenerator> logger) : IReserveGenerationTrigger, IDisposable
{
    private readonly ReserveOptions options = options.Value;
    private readonly CommandHookOptions hookOptions = commandHookOptions.Value;
    private readonly SemaphoreSlim gate = new(1, 1);

    /// <summary>
    /// 生成を 1 度だけ走らせる。同時に走らせても最後の 1 回が勝つだけなので、直列化する。
    /// </summary>
    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            DateTimeOffset now = timeProvider.GetUtcNow();
            ReserveGenerationInput input = new(
                now,
                await LoadRulesAsync(cancellationToken),
                await LoadProgramsAsync(now, cancellationToken),
                await store.ListManualReservesAsync(cancellationToken),
                await LoadTunersAsync(cancellationToken),
                await LoadHistoryAsync(cancellationToken),
                await store.ListStatesAsync(cancellationToken));

            IReadOnlyList<ReserveAssignment> assignments = ReserveGenerationPolicy.Generate(input);

            // フックが 1 つも設定されていなければ、差分を取るためだけの一覧取得を省く。
            bool needsDiff = hookOptions.ReserveNewAdditionCommand is not null
                || hookOptions.ReserveUpdateCommand is not null
                || hookOptions.ReserveDeletedCommand is not null;
            IReadOnlyDictionary<string, StoredReserve>? before = needsDiff
                ? await LoadStoredByKeyAsync(cancellationToken)
                : null;

            await store.ReplaceAsync(assignments, now, cancellationToken);
            await recordingSchedule.RequestAsync(cancellationToken);

            // 上流は予約更新イベントで ipc.notifyClient() を呼ぶ (EventSetter の
            // reserveEvent.setUpdated)。画面の予約一覧が黙って古いままになるのを防ぐ。
            notifier.NotifyClient();

            if (before is not null)
            {
                // 置き換え後の実 id (RESERVEID) を渡すため、置き換えの後にもう一度読み直す。
                IReadOnlyDictionary<string, StoredReserve> after = await LoadStoredByKeyAsync(cancellationToken);
                await FireReserveHooksAsync(before, after, cancellationToken);
            }

            // 番組表の更新のたびに呼ばれるので、重複回避の記憶の掃除もここに相乗りさせる。
            DateTimeOffset historyThreshold = now.AddDays(-Math.Max(0, options.RecordedHistoryRetentionPeriodDays));
            await history.PurgeAsync(historyThreshold, cancellationToken);

            if (!options.IsSuppressReservesUpdateAllLog)
            {
                int conflicts = assignments.Count(assignment => assignment.IsConflict);
                LogGenerated(logger, assignments.Count, conflicts);
            }

            return assignments.Count;
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<IReadOnlyList<RecordingRule>> LoadRulesAsync(CancellationToken cancellationToken)
    {
        Page<RecordingRule> page = await rules.ListAsync(
            new RuleQuery(Offset: 0, Limit: int.MaxValue),
            cancellationToken);
        return page.Items;
    }

    private async Task<IReadOnlyList<EpgProgram>> LoadProgramsAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        // 先の番組表まで見ても、放送までに内容が変わって作り直しになる。予約として意味を持つ
        // 範囲だけを読む。
        return await epg.FindProgramsAsync(
            new EpgScheduleQuery(now, now.AddDays(Math.Max(1, options.HorizonDays)), ["GR", "BS", "CS", "SKY"]),
            cancellationToken);
    }

    private async Task<IReadOnlyList<TunerDevice>> LoadTunersAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<MirakurunTunerDto> tuners = await mirakurun.GetTunersAsync(cancellationToken);

        // 故障中と利用不可のチューナーは数に入れない。入れると、録れない予約が録れる予定に見える。
        return [.. tuners
            .Where(tuner => tuner is { IsAvailable: true, IsFault: false })
            .Select(tuner => new TunerDevice(tuner.Index, tuner.Types))];
    }

    private async Task<IReadOnlyList<RecordedHistoryItem>> LoadHistoryAsync(CancellationToken cancellationToken) =>
        await history.ListAsync(cancellationToken);

    /// <summary>
    /// 置き換え直前の予約を、生成をやり直しても変わらない鍵 (<see cref="StoredReserve.Key"/>) で
    /// 引けるようにする。置き換え後の集合と突き合わせて新規・更新・削除を見分けるため。
    ///
    /// 番組表と結合した値ではなく保存されている行そのものを読む。番組表は置き換えの前に
    /// 更新されているので、結合後の値で比べると「番組名が変わった」が両側とも新しい名前になり、
    /// 変化なしと判定されてしまう (更新フックが一度も鳴らない)。
    /// </summary>
    private async Task<IReadOnlyDictionary<string, StoredReserve>> LoadStoredByKeyAsync(
        CancellationToken cancellationToken)
    {
        IReadOnlyList<StoredReserve> stored = await store.ListStoredAsync(cancellationToken);
        Dictionary<string, StoredReserve> byKey = new(StringComparer.Ordinal);
        foreach (StoredReserve reserve in stored)
        {
            byKey[string.IsNullOrEmpty(reserve.Key) ? $"reserve:{reserve.Id}" : reserve.Key] = reserve;
        }

        return byKey;
    }

    /// <summary>
    /// 置き換え前後を鍵で突き合わせ、新規・内容が変わったもの・消えたものにだけフックを鳴らす。
    /// 手動予約の追加・更新・削除は各エンドポイントが自分で鳴らすので、ここでは触れない —
    /// 鍵は手動予約も含むが、内容は毎回同じはずなので変化なしと判定され、二重に鳴ることはない。
    /// </summary>
    private async Task FireReserveHooksAsync(
        IReadOnlyDictionary<string, StoredReserve> before,
        IReadOnlyDictionary<string, StoredReserve> after,
        CancellationToken cancellationToken)
    {
        Dictionary<long, EpgChannel> channels = (await epg.ListChannelsAsync(cancellationToken))
            .ToDictionary(channel => channel.Id);

        // 番組の説明はフックの環境変数に載るが、予約の行には持たせていない。鳴らす対象の
        // 番組だけを引く。番組表から消えた予約 (削除フック) では引けないので null になる。
        Dictionary<long, EpgProgram> programs = await LoadProgramsForHooksAsync(before, after, cancellationToken);

        foreach ((string key, StoredReserve current) in after)
        {
            ReserveHookPayload payload = CreatePayload(current, channels, programs);

            if (!before.TryGetValue(key, out StoredReserve? previous))
            {
                hooks.RunReserveHook(hookOptions.ReserveNewAdditionCommand, payload);
                continue;
            }

            bool changed = previous.ChannelId != current.ChannelId
                || previous.StartAt != current.StartAt
                || previous.EndAt != current.EndAt
                || !string.Equals(previous.Name, current.Name, StringComparison.Ordinal)
                || previous.IsSkip != current.IsSkip;
            if (changed)
            {
                hooks.RunReserveHook(hookOptions.ReserveUpdateCommand, payload);
            }
        }

        foreach ((string key, StoredReserve previous) in before)
        {
            if (after.ContainsKey(key))
            {
                continue;
            }

            hooks.RunReserveHook(
                hookOptions.ReserveDeletedCommand,
                CreatePayload(previous, channels, programs));
        }
    }

    private static ReserveHookPayload CreatePayload(
        StoredReserve reserve,
        Dictionary<long, EpgChannel> channels,
        Dictionary<long, EpgProgram> programs)
    {
        channels.TryGetValue(reserve.ChannelId, out EpgChannel? channel);
        EpgProgram? program = reserve.ProgramId is { } programId && programs.TryGetValue(programId, out EpgProgram? found)
            ? found
            : null;

        return new ReserveHookPayload(
            reserve.Id,
            reserve.ProgramId,
            reserve.ChannelId,
            channel?.Name ?? "CH",
            channel?.HalfWidthName ?? "CH",
            reserve.StartAt.ToUnixTimeMilliseconds(),
            reserve.EndAt.ToUnixTimeMilliseconds(),
            reserve.Name,
            reserve.HalfWidthName,
            program?.Description,
            program?.HalfWidthDescription,
            program?.Extended,
            program?.HalfWidthExtended,
            channel?.ChannelType ?? reserve.ChannelType);
    }

    private async Task<Dictionary<long, EpgProgram>> LoadProgramsForHooksAsync(
        IReadOnlyDictionary<string, StoredReserve> before,
        IReadOnlyDictionary<string, StoredReserve> after,
        CancellationToken cancellationToken)
    {
        long[] programIds = [.. before.Values.Concat(after.Values)
            .Select(reserve => reserve.ProgramId)
            .Where(id => id is not null)
            .Select(id => id!.Value)
            .Distinct()];

        if (programIds.Length == 0)
        {
            return [];
        }

        IReadOnlyList<EpgProgram> found = await epg.FindProgramsByIdsAsync(programIds, cancellationToken);
        return found.ToDictionary(program => program.Id);
    }

    public async ValueTask RequestAsync(CancellationToken cancellationToken) =>
        await RunAsync(cancellationToken);

    public void Dispose() => gate.Dispose();

    [LoggerMessage(
        EventId = 3000,
        Level = LogLevel.Information,
        Message = "Generated {ReserveCount} reserves, {ConflictCount} of which cannot be recorded")]
    private static partial void LogGenerated(ILogger logger, int reserveCount, int conflictCount);
}
