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

/// <summary>
/// 予約を作り直すのは 1 つだけ、という取り決め。番組表の同期とは別に持つ。同じものを使うと、
/// 同期を掴んでいる実体以外は予約を作り直せない。
/// </summary>
public interface IReserveGenerationLeaseProvider
{
    ValueTask<IAsyncDisposable?> TryAcquireAsync(CancellationToken cancellationToken);
}

/// <summary>
/// 録画するのは 1 つだけ、という取り決め。予約の生成とは別に持つ。生成は数分おきに終わる
/// 仕事だが、録画は録っている間ずっと握り続けるので、同じ鍵にすると生成が止まる。
/// </summary>
public interface IRecordingLeaseProvider
{
    ValueTask<IAsyncDisposable?> TryAcquireAsync(CancellationToken cancellationToken);
}

public interface IChannelLogoProvider
{
    ValueTask<ReadOnlyMemory<byte>> GetLogoAsync(long channelId, CancellationToken cancellationToken);
}
