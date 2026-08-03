using TNLAStation.Application.Abstractions;
using TNLAStation.Application.Models;
using TNLAStation.Domain;

namespace TNLAStation.Infrastructure.Repositories;

public sealed class InMemoryReserveRepository : IReserveRepository
{
    private readonly object gate = new();
    private readonly TimeProvider timeProvider;
    private readonly List<Reservation> reserves;
    private long nextId = 1;

    public InMemoryReserveRepository(TimeProvider timeProvider)
    {
        this.timeProvider = timeProvider;

        // 固定の過去日時だと、実行するたびに経過時間で終了済みになり ListAsync から消えてしまう。
        // 常に「これから」に見えるよう、起動時刻からの相対時刻でモックする。
        DateTimeOffset mockStart = timeProvider.GetUtcNow().AddHours(1);
        reserves =
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
                StartAt: mockStart.ToUnixTimeMilliseconds(),
                EndAt: mockStart.AddMinutes(30).ToUnixTimeMilliseconds(),
                Name: "モック予約番組",
                HalfWidthName: "モック予約番組",
                RawExtended: new Dictionary<string, string> { ["補足"] = "固定データ" })
        ];
    }

    public ValueTask<Page<Reservation>> ListAsync(ReserveQuery query, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (gate)
        {
            // EPGStation は録画が終わった予約を Recorded へ移して Reserve からは消す。この実装はまだ
            // 録画完了時にその移動をしないので、代わりに読むときに終了済みを外して同じ見た目にする。
            long nowMs = timeProvider.GetUtcNow().ToUnixTimeMilliseconds();
            IEnumerable<Reservation> result = reserves.Where(item => item.EndAt > nowMs);

            result = query.Type switch
            {
                "normal" => result.Where(IsNormal),
                "conflict" => result.Where(item => item.IsConflict),
                "skip" => result.Where(item => item.IsSkip),
                "overlap" => result.Where(item => item.IsOverlap),
                _ => result
            };

            if (query.RuleId == 0)
            {
                result = result.Where(item => item.RuleId is null);
            }
            else if (query.RuleId is not null)
            {
                result = result.Where(item => item.RuleId == query.RuleId);
            }

            if (query.ChannelId is not null)
            {
                result = result.Where(item => item.ChannelId == query.ChannelId);
            }

            if (query.Genre is not null)
            {
                result = result.Where(item =>
                    item.Genre1 == query.Genre || item.Genre2 == query.Genre || item.Genre3 == query.Genre);
            }

            if (!string.IsNullOrWhiteSpace(query.Keyword))
            {
                result = result.Where(item =>
                    item.Name.Contains(query.Keyword, StringComparison.OrdinalIgnoreCase) ||
                    item.HalfWidthName.Contains(query.Keyword, StringComparison.OrdinalIgnoreCase) ||
                    (item.Description?.Contains(query.Keyword, StringComparison.OrdinalIgnoreCase) ?? false) ||
                    (item.HalfWidthDescription?.Contains(query.Keyword, StringComparison.OrdinalIgnoreCase) ?? false) ||
                    (item.Extended?.Contains(query.Keyword, StringComparison.OrdinalIgnoreCase) ?? false) ||
                    (item.HalfWidthExtended?.Contains(query.Keyword, StringComparison.OrdinalIgnoreCase) ?? false));
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
            // EPGStation と同じ非対称な拒否: 番組指定の手動予約は、既に何か (手動でもルールでも) が
            // 同じ番組を掴んでいれば拒否する。逆にルール側からの二重取得は防がない。
            if (command.ProgramId is { } programId &&
                reserves.Any(item => item.ProgramId == programId))
            {
                throw new InvalidOperationException("ReservationManageModelReservedError");
            }

            if (command.TimeSpecified is { } specifiedCheck)
            {
                if (specifiedCheck.EndAt <= timeProvider.GetUtcNow().ToUnixTimeMilliseconds())
                {
                    throw new InvalidOperationException("TimeSpecifiedOptionError");
                }

                bool duplicate = reserves.Any(item =>
                    item.RuleId is null &&
                    item.ChannelId == specifiedCheck.ChannelId &&
                    item.StartAt == specifiedCheck.StartAt &&
                    item.EndAt == specifiedCheck.EndAt);
                if (duplicate)
                {
                    throw new InvalidOperationException("AddReservationConflictError");
                }
            }

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

    public ValueTask<Reservation?> GetAsync(long reserveId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (gate)
        {
            return ValueTask.FromResult(reserves.SingleOrDefault(item => item.Id == reserveId));
        }
    }

    public ValueTask<bool> DeleteAsync(long reserveId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (gate)
        {
            return ValueTask.FromResult(reserves.RemoveAll(item => item.Id == reserveId) > 0);
        }
    }

    public ValueTask<bool> SetSkipAsync(long reserveId, bool isSkip, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (gate)
        {
            int index = reserves.FindIndex(item => item.Id == reserveId);
            if (index < 0)
            {
                return ValueTask.FromResult(false);
            }

            reserves[index] = reserves[index] with { IsSkip = isSkip };
            return ValueTask.FromResult(true);
        }
    }

    public ValueTask<bool> ClearOverlapAsync(long reserveId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (gate)
        {
            int index = reserves.FindIndex(item => item.Id == reserveId);
            if (index < 0)
            {
                return ValueTask.FromResult(false);
            }

            reserves[index] = reserves[index] with { IsOverlap = false };
            return ValueTask.FromResult(true);
        }
    }

    public ValueTask<bool> UpdateAsync(
        long reserveId,
        CreateReserveCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        lock (gate)
        {
            int index = reserves.FindIndex(item => item.Id == reserveId);
            if (index < 0)
            {
                return ValueTask.FromResult(false);
            }

            reserves[index] = reserves[index] with
            {
                AllowEndLack = command.AllowEndLack,
                Priority = command.Priority,
                Tags = command.Tags,
                ParentDirectoryName = command.Save?.ParentDirectoryName,
                Directory = command.Save?.Directory,
                RecordedFormat = command.Save?.RecordedFormat,
            };
            return ValueTask.FromResult(true);
        }
    }

    private static bool IsNormal(Reservation item) =>
        !item.IsConflict && !item.IsSkip && !item.IsOverlap;
}
