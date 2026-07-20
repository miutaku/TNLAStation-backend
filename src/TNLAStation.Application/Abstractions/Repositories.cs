using TNLAStation.Application.Models;
using TNLAStation.Domain;

namespace TNLAStation.Application.Abstractions;

public interface IConfigRepository
{
    ValueTask<StationConfiguration> GetAsync(CancellationToken cancellationToken);
}

public interface IRecordedRepository
{
    ValueTask<Page<RecordedProgram>> ListAsync(RecordedQuery query, CancellationToken cancellationToken);

    ValueTask<long> AddAsync(CreateRecordedCommand command, CancellationToken cancellationToken);
}

public interface IReserveRepository
{
    ValueTask<Page<Reservation>> ListAsync(ReserveQuery query, CancellationToken cancellationToken);

    ValueTask<long> AddAsync(CreateReserveCommand command, CancellationToken cancellationToken);
}

public interface IStorageRepository
{
    ValueTask<IReadOnlyList<StorageUsage>> ListAsync(CancellationToken cancellationToken);
}

public interface IVersionRepository
{
    ValueTask<string> GetAsync(CancellationToken cancellationToken);
}
