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
    IEncodeJobRegistry jobRegistry,
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
        // DB が遅いときも先にプロセスへ停止を伝える。削除との間にワーカーが登録される
        // 競合もあるため、削除後にもう一度確認する。
        Task? stopped = jobRegistry.RequestCancel(encodeId);

        await using EpgDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);
        int removed = await context.EncodeTasks
            .Where(task => task.Id == encodeId)
            .ExecuteDeleteAsync(cancellationToken);

        stopped ??= jobRegistry.RequestCancel(encodeId);
        if (stopped is not null)
        {
            // 204 を返した後も ffmpeg が走り続けないよう、ワーカーがプロセスツリーと
            // 中間ファイルを片付けるまで待つ。
            await stopped.WaitAsync(StopTimeout, cancellationToken);
        }

        return removed > 0;
    }

    public async ValueTask<int> CancelForRecordedAsync(long recordedId, CancellationToken cancellationToken)
    {
        long[] ids;
        var stopped = new HashSet<Task>();
        await using (EpgDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken))
        {
            ids = await context.EncodeTasks
                .Where(task => task.RecordedId == recordedId)
                .Select(task => task.Id)
                .ToArrayAsync(cancellationToken);

            foreach (long id in ids)
            {
                if (jobRegistry.RequestCancel(id) is { } completion)
                {
                    stopped.Add(completion);
                }
            }

            await context.EncodeTasks
                .Where(task => task.RecordedId == recordedId)
                .ExecuteDeleteAsync(cancellationToken);
        }

        foreach (long id in ids)
        {
            if (jobRegistry.RequestCancel(id) is { } completion)
            {
                stopped.Add(completion);
            }
        }

        if (stopped.Count > 0)
        {
            await Task.WhenAll(stopped).WaitAsync(StopTimeout, cancellationToken);
        }

        return ids.Length;
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
