using TNLAStation.Application.Abstractions;
using TNLAStation.Application.Models;
using TNLAStation.Domain;

namespace TNLAStation.Infrastructure.Repositories;

/// <summary>
/// 録画・エンコード・配信・tag の実体はこれから作る。それまでの間も、これらは「まだ無い」のでは
/// なく「いま空」なのだから、空の一覧を返す。実装が入る時点でこの型を差し替えるだけで済む。
/// </summary>
public sealed class EmptyRecordingRepository : IRecordingRepository
{
    public ValueTask<Page<RecordedProgram>> ListAsync(RecordedQuery query, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(new Page<RecordedProgram>([], 0));
    }
}

public sealed class EmptyEncodeQueueRepository : IEncodeQueueRepository
{
    public ValueTask<EncodeTasks> GetAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(new EncodeTasks([], []));
    }
}

public sealed class EmptyStreamRepository : IStreamRepository
{
    public ValueTask<IReadOnlyList<StreamSession>> ListAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult<IReadOnlyList<StreamSession>>([]);
    }
}

public sealed class EmptyRecordedTagRepository : IRecordedTagRepository
{
    public ValueTask<Page<RecordedTag>> ListAsync(RecordedTagQuery query, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(new Page<RecordedTag>([], 0));
    }
}

/// <summary>
/// 録画を保存する場所を持たない構成。録画は 1 件も無いので、どれを指しても見つからない。
/// tag も残す先が無いので作れない。
/// </summary>
public sealed class UnavailableRecordedItemRepository : IRecordedItemRepository, IRecordedTagWriteRepository
{
    public ValueTask<RecordedProgram?> GetAsync(long recordedId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult<RecordedProgram?>(null);
    }

    public ValueTask<bool> DeleteAsync(long recordedId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(false);
    }

    public ValueTask<bool> SetProtectedAsync(
        long recordedId,
        bool isProtected,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(false);
    }

    public ValueTask<long> AddTagAsync(string name, string color, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("RecordedStoreIsNotConfigured");

    public ValueTask<bool> UpdateTagAsync(
        long tagId,
        string name,
        string color,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(false);
    }

    public ValueTask<bool> DeleteTagAsync(long tagId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(false);
    }

    public ValueTask<bool> SetTagAsync(
        long recordedId,
        long tagId,
        bool attached,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(false);
    }
}
