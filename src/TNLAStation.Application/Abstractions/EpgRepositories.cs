using TNLAStation.Application.Models;
using TNLAStation.Domain;

namespace TNLAStation.Application.Abstractions;

public interface IEpgRepository
{
    ValueTask<IReadOnlyList<EpgChannel>> ListChannelsAsync(CancellationToken cancellationToken);

    ValueTask<EpgChannel?> GetChannelAsync(long channelId, CancellationToken cancellationToken);

    ValueTask<EpgProgram?> GetProgramAsync(long programId, CancellationToken cancellationToken);

    ValueTask<IReadOnlyList<EpgProgram>> FindProgramsAsync(
        EpgScheduleQuery query,
        CancellationToken cancellationToken);

    ValueTask<IReadOnlyList<EpgProgram>> SearchProgramsAsync(
        EpgSearchQuery query,
        CancellationToken cancellationToken);
}

public interface IEpgStore
{
    ValueTask ReplaceSnapshotAsync(EpgSnapshot snapshot, CancellationToken cancellationToken);

    ValueTask ApplyChangesAsync(
        IReadOnlyList<EpgChannel> changedChannels,
        IReadOnlyList<EpgProgram> upsertPrograms,
        IReadOnlyList<long> deleteProgramIds,
        DateTimeOffset streamEventAt,
        CancellationToken cancellationToken);

    ValueTask DeleteProgramsEndingBeforeAsync(DateTimeOffset threshold, CancellationToken cancellationToken);

    ValueTask RecordSyncFailureAsync(
        DateTimeOffset attemptedAt,
        string failureMessage,
        CancellationToken cancellationToken);
}

public interface IEpgSyncLeaseProvider
{
    ValueTask<IAsyncDisposable?> TryAcquireAsync(CancellationToken cancellationToken);
}

public interface IChannelLogoProvider
{
    ValueTask<ReadOnlyMemory<byte>> GetLogoAsync(long channelId, CancellationToken cancellationToken);
}
