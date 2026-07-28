using Microsoft.EntityFrameworkCore;
using TNLAStation.Application.Abstractions;
using TNLAStation.Application.Models;
using TNLAStation.Infrastructure.Persistence;

namespace TNLAStation.Infrastructure.Repositories;

public sealed class PostgresRecordedHistoryStore(IDbContextFactory<EpgDbContext> contextFactory) : IRecordedHistoryStore
{
    public async ValueTask AddAsync(string name, long channelId, DateTimeOffset endAt, CancellationToken cancellationToken)
    {
        await using EpgDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);
        context.RecordedHistory.Add(new RecordedHistoryEntity
        {
            Name = name,
            ChannelId = channelId,
            EndAt = endAt,
        });
        await context.SaveChangesAsync(cancellationToken);
    }

    public async ValueTask<IReadOnlyList<RecordedHistoryItem>> ListAsync(CancellationToken cancellationToken)
    {
        await using EpgDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.RecordedHistory.AsNoTracking()
            .Select(item => new RecordedHistoryItem(item.Name, item.ChannelId, item.EndAt))
            .ToArrayAsync(cancellationToken);
    }

    public async ValueTask<int> PurgeAsync(DateTimeOffset threshold, CancellationToken cancellationToken)
    {
        await using EpgDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.RecordedHistory
            .Where(item => item.EndAt < threshold)
            .ExecuteDeleteAsync(cancellationToken);
    }
}
