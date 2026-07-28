using TNLAStation.Application.Abstractions;
using TNLAStation.Application.Models;

namespace TNLAStation.Infrastructure.Repositories;

/// <summary>PostgreSQL が無い構成向け。プロセスの寿命でしか覚えていない。</summary>
public sealed class InMemoryRecordedHistoryStore : IRecordedHistoryStore
{
    private readonly object gate = new();
    private readonly List<RecordedHistoryItem> items = [];

    public ValueTask AddAsync(string name, long channelId, DateTimeOffset endAt, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            items.Add(new RecordedHistoryItem(name, channelId, endAt));
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask<IReadOnlyList<RecordedHistoryItem>> ListAsync(CancellationToken cancellationToken)
    {
        lock (gate)
        {
            return ValueTask.FromResult<IReadOnlyList<RecordedHistoryItem>>([.. items]);
        }
    }

    public ValueTask<int> PurgeAsync(DateTimeOffset threshold, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            int removed = items.RemoveAll(item => item.EndAt < threshold);
            return ValueTask.FromResult(removed);
        }
    }
}
