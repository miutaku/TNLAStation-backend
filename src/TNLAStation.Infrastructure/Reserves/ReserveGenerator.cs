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
    IRecordedRepository recorded,
    IReserveStore store,
    IMirakurunClient mirakurun,
    IOptions<ReserveOptions> options,
    TimeProvider timeProvider,
    ILogger<ReserveGenerator> logger) : IReserveGenerationTrigger, IDisposable
{
    private readonly ReserveOptions options = options.Value;
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
            await store.ReplaceAsync(assignments, now, cancellationToken);

            int conflicts = assignments.Count(assignment => assignment.IsConflict);
            LogGenerated(logger, assignments.Count, conflicts);
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

    private async Task<IReadOnlyList<RecordedHistoryItem>> LoadHistoryAsync(CancellationToken cancellationToken)
    {
        Page<RecordedProgram> page = await recorded.ListAsync(
            new RecordedQuery(IsHalfWidth: false, Offset: 0, Limit: int.MaxValue),
            cancellationToken);

        return [.. page.Items.Select(item => new RecordedHistoryItem(
            item.Name,
            item.ChannelId,
            DateTimeOffset.FromUnixTimeMilliseconds(item.EndAt)))];
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
