using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using TNLAStation.Application.Abstractions;
using TNLAStation.Infrastructure.Persistence;

namespace TNLAStation.Infrastructure.Recording;

/// <summary>
/// 録画 ID から元の予約を取り消し、受信ストリームの停止と途中ファイルの確定を待つ。
/// </summary>
internal sealed class PostgresRecordingStopService(
    IDbContextFactory<EpgDbContext> contextFactory,
    IReserveRepository reserves,
    IRecordingJobRegistry jobs) : IRecordingStopService
{
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(50);

    public async ValueTask<bool> StopAsync(long recordedId, CancellationToken cancellationToken)
    {
        RecordingJobIdentity? active = jobs.Find(recordedId);
        StopTarget? target = await FindTargetAsync(recordedId, active, cancellationToken);
        if (target is null)
        {
            return false;
        }

        // セッションより先に予約を止める。逆順だと、次の Tick が同じ予約をもう一度開始できる。
        await CancelReserveAsync(target, cancellationToken);

        Task? completion = jobs.RequestStop(recordedId);
        if (completion is not null)
        {
            await completion.WaitAsync(cancellationToken);
        }
        else
        {
            // BeginAsync が行を公開してから registry へ登録するまでの僅かな隙間と、別実体で
            // Scheduler が動いている構成の両方を扱う。登録されたら即座に止め、別実体なら
            // 予約が消えたことを Tick が検知して IsRecording を閉じるまで待つ。
            await WaitForStopAsync(recordedId, cancellationToken);
        }

        // 予約生成と重なって ID が差し替わっていた場合も、安定キーで新しい行を探して
        // 既存 DeleteAsync を通す。ルール予約なら skip、手動予約なら元データ削除になる。
        await CancelReserveAsync(target, cancellationToken);
        return true;
    }

    private async Task WaitForStopAsync(long recordedId, CancellationToken cancellationToken)
    {
        long startedAt = Stopwatch.GetTimestamp();
        while (Stopwatch.GetElapsedTime(startedAt) < StopTimeout)
        {
            Task? completion = jobs.RequestStop(recordedId);
            if (completion is not null)
            {
                await completion.WaitAsync(cancellationToken);
                return;
            }

            if (!await IsRecordingAsync(recordedId, cancellationToken))
            {
                return;
            }

            await Task.Delay(PollInterval, cancellationToken);
        }

        throw new TimeoutException($"Recording {recordedId} did not stop within {StopTimeout}.");
    }

    private async ValueTask<StopTarget?> FindTargetAsync(
        long recordedId,
        RecordingJobIdentity? active,
        CancellationToken cancellationToken)
    {
        await using EpgDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);
        RecordedStopRow? recorded = await context.Recorded.AsNoTracking()
            .Where(item => item.Id == recordedId)
            .Select(item => new RecordedStopRow(
                item.IsRecording,
                item.ReserveId,
                item.ReserveKey,
                item.ManualReserveId,
                item.ProgramId,
                item.RuleId,
                item.ChannelId,
                item.StartAt,
                item.EndAt,
                item.Name))
            .SingleOrDefaultAsync(cancellationToken);
        if (recorded is not { IsRecording: true })
        {
            return null;
        }

        long? reserveId = active?.ReserveId ?? recorded.ReserveId;
        string? reserveKey = active?.ReserveKey ?? recorded.ReserveKey;
        long? manualReserveId = active?.ManualReserveId ?? recorded.ManualReserveId;

        ReserveIdentityRow? current = null;
        if (!string.IsNullOrWhiteSpace(reserveKey))
        {
            current = await context.Reserves.AsNoTracking()
                .Where(item => item.Key == reserveKey)
                .Select(item => new ReserveIdentityRow(item.Id, item.Key, item.ManualReserveId))
                .SingleOrDefaultAsync(cancellationToken);
        }

        if (current is null && reserveId is { } originalReserveId)
        {
            current = await context.Reserves.AsNoTracking()
                .Where(item => item.Id == originalReserveId)
                .Select(item => new ReserveIdentityRow(item.Id, item.Key, item.ManualReserveId))
                .SingleOrDefaultAsync(cancellationToken);
        }

        // 更新前に始まった録画など、安定キーをまだ持たない行だけに使う後方互換経路。
        // 番組 ID を優先し、時刻指定は局・時刻・名前が全て一致するものだけを採る。
        if (current is null && string.IsNullOrWhiteSpace(reserveKey))
        {
            IQueryable<ReserveEntity> candidates = context.Reserves.AsNoTracking();
            candidates = recorded.ProgramId is { } programId
                ? candidates.Where(item => item.ProgramId == programId && item.RuleId == recorded.RuleId)
                : candidates.Where(item =>
                    item.ChannelId == recorded.ChannelId &&
                    item.StartAt == recorded.StartAt &&
                    item.EndAt == recorded.EndAt &&
                    item.Name == recorded.Name);
            ReserveIdentityRow[] matches = await candidates
                .Select(item => new ReserveIdentityRow(item.Id, item.Key, item.ManualReserveId))
                .Take(2)
                .ToArrayAsync(cancellationToken);
            current = matches.Length == 1 ? matches[0] : null;
        }

        reserveId ??= current?.Id;
        reserveKey ??= current?.Key;
        manualReserveId ??= current?.ManualReserveId;
        return reserveId is null || string.IsNullOrWhiteSpace(reserveKey)
            ? null
            : new StopTarget(reserveId.Value, reserveKey, manualReserveId);
    }

    private async ValueTask CancelReserveAsync(StopTarget target, CancellationToken cancellationToken)
    {
        // 必ず既存経路を通す。ここが手動予約の削除とルール予約の skip を正しく分岐する。
        await reserves.DeleteAsync(target.ReserveId, cancellationToken);

        await using EpgDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);
        long? currentId = await context.Reserves.AsNoTracking()
            .Where(item => item.Key == target.ReserveKey)
            .Select(item => (long?)item.Id)
            .SingleOrDefaultAsync(cancellationToken);
        if (currentId is { } replacementId && replacementId != target.ReserveId)
        {
            await reserves.DeleteAsync(replacementId, cancellationToken);
        }
    }

    private async ValueTask<bool> IsRecordingAsync(long recordedId, CancellationToken cancellationToken)
    {
        await using EpgDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.Recorded.AsNoTracking()
            .AnyAsync(item => item.Id == recordedId && item.IsRecording, cancellationToken);
    }

    private sealed record StopTarget(long ReserveId, string ReserveKey, long? ManualReserveId);

    private sealed record ReserveIdentityRow(long Id, string Key, long? ManualReserveId);

    private sealed record RecordedStopRow(
        bool IsRecording,
        long? ReserveId,
        string? ReserveKey,
        long? ManualReserveId,
        long? ProgramId,
        long? RuleId,
        long ChannelId,
        DateTimeOffset StartAt,
        DateTimeOffset EndAt,
        string Name);
}
