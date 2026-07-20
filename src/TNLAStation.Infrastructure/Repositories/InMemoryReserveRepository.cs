using TNLAStation.Application.Abstractions;
using TNLAStation.Application.Models;
using TNLAStation.Domain;

namespace TNLAStation.Infrastructure.Repositories;

public sealed class InMemoryReserveRepository : IReserveRepository
{
    private readonly object gate = new();
    private readonly List<Reservation> reserves =
    [
        new Reservation(
            Id: 1,
            IsSkip: false,
            IsConflict: false,
            IsOverlap: false,
            AllowEndLack: false,
            IsTimeSpecified: true,
            IsDeleteOriginalAfterEncode: false,
            ChannelId: 1,
            StartAt: 1_735_696_800_000,
            EndAt: 1_735_698_600_000,
            Name: "モック予約番組",
            HalfWidthName: "モック予約番組",
            RawExtended: new Dictionary<string, string> { ["補足"] = "固定データ" })
    ];
    private long nextId = 1;

    public ValueTask<Page<Reservation>> ListAsync(ReserveQuery query, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (gate)
        {
            IEnumerable<Reservation> result = reserves;

            result = query.Type switch
            {
                "normal" => result.Where(IsNormal),
                "conflict" => result.Where(item => item.IsConflict),
                "skip" => result.Where(item => item.IsSkip),
                "overlap" => result.Where(item => item.IsOverlap),
                _ => result
            };

            if (query.RuleId is not null)
            {
                result = result.Where(item => item.RuleId == query.RuleId);
            }

            Reservation[] materialized = result.OrderBy(item => item.StartAt).ToArray();
            Reservation[] page = materialized.Skip(query.Offset).Take(query.Limit).ToArray();
            return ValueTask.FromResult(new Page<Reservation>(page, materialized.Length));
        }
    }

    public ValueTask<long> AddAsync(CreateReserveCommand command, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (gate)
        {
            long id = ++nextId;
            TimeSpecifiedReserve? specified = command.TimeSpecified;
            reserves.Add(new Reservation(
                Id: id,
                IsSkip: false,
                IsConflict: false,
                IsOverlap: false,
                AllowEndLack: command.AllowEndLack,
                IsTimeSpecified: specified is not null,
                IsDeleteOriginalAfterEncode: command.Encode?.IsDeleteOriginalAfterEncode ?? false,
                ChannelId: specified?.ChannelId ?? 0,
                StartAt: specified?.StartAt ?? 0,
                EndAt: specified?.EndAt ?? 0,
                Name: specified?.Name ?? "モック番組予約",
                HalfWidthName: specified?.Name ?? "モック番組予約",
                Tags: command.Tags,
                ParentDirectoryName: command.Save?.ParentDirectoryName,
                Directory: command.Save?.Directory,
                RecordedFormat: command.Save?.RecordedFormat,
                EncodeMode1: command.Encode?.Mode1,
                EncodeParentDirectoryName1: command.Encode?.EncodeParentDirectoryName1,
                EncodeDirectory1: command.Encode?.Directory1,
                EncodeMode2: command.Encode?.Mode2,
                EncodeParentDirectoryName2: command.Encode?.EncodeParentDirectoryName2,
                EncodeDirectory2: command.Encode?.Directory2,
                EncodeMode3: command.Encode?.Mode3,
                EncodeParentDirectoryName3: command.Encode?.EncodeParentDirectoryName3,
                EncodeDirectory3: command.Encode?.Directory3,
                ProgramId: command.ProgramId));

            return ValueTask.FromResult(id);
        }
    }

    private static bool IsNormal(Reservation item) =>
        !item.IsConflict && !item.IsSkip && !item.IsOverlap;
}
