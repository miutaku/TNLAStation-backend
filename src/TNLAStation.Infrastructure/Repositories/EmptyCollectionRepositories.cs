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
