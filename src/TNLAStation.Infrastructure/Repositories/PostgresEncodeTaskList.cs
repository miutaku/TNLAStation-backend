using Microsoft.EntityFrameworkCore;
using TNLAStation.Application.Abstractions;
using TNLAStation.Application.Models;
using TNLAStation.Domain;
using TNLAStation.Infrastructure.Persistence;
using ApplicationEncodeTask = TNLAStation.Application.Abstractions.EncodeTask;

namespace TNLAStation.Infrastructure.Repositories;

public sealed class PostgresEncodeTaskList(
    IDbContextFactory<EpgDbContext> contextFactory,
    IRecordedItemRepository recorded,
    TimeProvider timeProvider) : IEncodeTaskList, IEncodeQueueRepository
{
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(15);

    public async ValueTask<long> EnqueueAsync(EncodeRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        await using EpgDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);
        bool sourceExists = await context.VideoFiles.AnyAsync(
            file => file.Id == request.SourceVideoFileId && file.RecordedId == request.RecordedId,
            cancellationToken);
        if (!sourceExists)
        {
            throw new InvalidOperationException("VideoFileIsNotFound");
        }

        var entity = new EncodeTaskEntity
        {
            RecordedId = request.RecordedId,
            SourceVideoFileId = request.SourceVideoFileId,
            Mode = request.Mode,
            ParentDirectoryName = request.IsSaveSameDirectory ? null : request.ParentDirectoryName,
            Directory = request.Directory,
            RemoveOriginal = request.RemoveOriginal,
            Status = "waiting",
            CancelRequested = false,
            CreatedAt = timeProvider.GetUtcNow(),
        };
        context.EncodeTasks.Add(entity);
        await context.SaveChangesAsync(cancellationToken);
        return entity.Id;
    }

    public async ValueTask<IReadOnlyList<ApplicationEncodeTask>> ListAsync(CancellationToken cancellationToken)
    {
        await using EpgDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);
        EncodeTaskEntity[] items = await context.EncodeTasks.AsNoTracking()
            .OrderBy(task => task.Id)
            .ToArrayAsync(cancellationToken);

        return [.. items.Select(task => new ApplicationEncodeTask(
            task.Id,
            task.RecordedId,
            task.SourceVideoFileId,
            task.Mode,
            task.RemoveOriginal,
            task.ParentDirectoryName,
            task.Directory,
            task.Status == "running",
            task.Percent,
            task.Log))];
    }

    public async ValueTask<bool> CancelAsync(long encodeId, CancellationToken cancellationToken)
    {
        await using EpgDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);
        int waitingRemoved = await context.EncodeTasks
            .Where(task => task.Id == encodeId && task.Status == "waiting")
            .ExecuteDeleteAsync(cancellationToken);
        if (waitingRemoved == 1)
        {
            return true;
        }

        int requested = await context.EncodeTasks
            .Where(task => task.Id == encodeId && task.Status == "running")
            .ExecuteUpdateAsync(
                update => update.SetProperty(task => task.CancelRequested, true),
                cancellationToken);
        if (requested != 1)
        {
            return false;
        }

        await WaitForWorkerStopAsync([encodeId], cancellationToken);
        return true;
    }

    public async ValueTask<int> CancelForRecordedAsync(long recordedId, CancellationToken cancellationToken)
    {
        long[] ids;
        await using (EpgDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken))
        {
            ids = await context.EncodeTasks
                .Where(task => task.RecordedId == recordedId)
                .Select(task => task.Id)
                .ToArrayAsync(cancellationToken);

            await context.EncodeTasks
                .Where(task => task.RecordedId == recordedId && task.Status == "waiting")
                .ExecuteDeleteAsync(cancellationToken);
            await context.EncodeTasks
                .Where(task => task.RecordedId == recordedId && task.Status == "running")
                .ExecuteUpdateAsync(
                    update => update.SetProperty(task => task.CancelRequested, true),
                    cancellationToken);
        }

        await WaitForWorkerStopAsync(ids, cancellationToken);
        return ids.Length;
    }

    private async Task WaitForWorkerStopAsync(
        long[] ids,
        CancellationToken cancellationToken)
    {
        if (ids.Length == 0)
        {
            return;
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(StopTimeout);
        try
        {
            while (true)
            {
                await using EpgDbContext context = await contextFactory.CreateDbContextAsync(timeout.Token);
                bool exists = await context.EncodeTasks.AnyAsync(
                    task => ids.Contains(task.Id),
                    timeout.Token);
                if (!exists)
                {
                    return;
                }

                await Task.Delay(TimeSpan.FromMilliseconds(200), timeProvider, timeout.Token);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            await using EpgDbContext context = await contextFactory.CreateDbContextAsync(CancellationToken.None);
            await context.EncodeTasks
                .Where(task => ids.Contains(task.Id) && task.CancelRequested)
                .ExecuteDeleteAsync(CancellationToken.None);
        }
    }

    /// <summary>
    /// 画面へ出す形。待ちと実行中に分け、それぞれに録画の中身を添える。
    /// </summary>
    public async ValueTask<EncodeTasks> GetAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<ApplicationEncodeTask> tasks = await ListAsync(cancellationToken);
        if (tasks.Count == 0)
        {
            return new EncodeTasks([], []);
        }

        var running = new List<EncodeQueueItem>();
        var waiting = new List<EncodeQueueItem>();
        foreach (ApplicationEncodeTask task in tasks)
        {
            RecordedProgram? program = await recorded.GetAsync(task.RecordedId, cancellationToken);
            if (program is null)
            {
                // 録画が消えていれば出す先がない。行の掃除は取り消しの経路に任せる。
                continue;
            }

            var item = new EncodeQueueItem(task.Id, task.Mode, program, task.Percent, task.Log);
            (task.IsRunning ? running : waiting).Add(item);
        }

        return new EncodeTasks(running, waiting);
    }
}
