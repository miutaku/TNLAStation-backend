using TNLAStation.Application.Models;

namespace TNLAStation.Application.Abstractions;

/// <summary>
/// ルール録画の重複回避のための記憶。<see cref="IRecordedRepository"/> とは別に持つ —
/// 録画本体を消しても、保持期間の間は「録った」という事実だけ覚えておきたいため。
/// </summary>
public interface IRecordedHistoryStore
{
    ValueTask AddAsync(string name, long channelId, DateTimeOffset endAt, CancellationToken cancellationToken);

    ValueTask<IReadOnlyList<RecordedHistoryItem>> ListAsync(CancellationToken cancellationToken);

    /// <summary>指定より古い記憶を消す。戻り値は消した件数。</summary>
    ValueTask<int> PurgeAsync(DateTimeOffset threshold, CancellationToken cancellationToken);
}
