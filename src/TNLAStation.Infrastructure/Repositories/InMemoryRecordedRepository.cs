using TNLAStation.Application.Abstractions;
using TNLAStation.Application.Models;
using TNLAStation.Domain;

namespace TNLAStation.Infrastructure.Repositories;

public sealed class InMemoryRecordedRepository : IRecordedRepository
{
    private readonly object gate = new();
    private readonly List<RecordedProgram> records =
    [
        new RecordedProgram(
            Id: 1,
            ChannelId: 1,
            StartAt: 1_735_689_600_000,
            EndAt: 1_735_691_400_000,
            Name: "モック録画番組",
            HalfWidthName: "モック録画番組",
            IsRecording: false,
            IsEncoding: false,
            IsProtected: false,
            Description: "フェーズ1の固定録画データです。",
            HalfWidthDescription: "フェーズ1の固定録画データです。",
            RawExtended: new Dictionary<string, string> { ["補足"] = "固定データ" },
            Genre1: 0,
            SubGenre1: 0,
            Thumbnails: [],
            VideoFiles: [])
    ];
    private long nextId = 1;

    public ValueTask<Page<RecordedProgram>> ListAsync(RecordedQuery query, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (gate)
        {
            IEnumerable<RecordedProgram> result = records;

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

            if (query.HasOriginalFile is not null)
            {
                result = result.Where(item =>
                    (item.VideoFiles?.Any(file => file.Type == "ts") ?? false) == query.HasOriginalFile.Value);
            }

            result = query.IsReverse == true
                ? result.OrderBy(item => item.StartAt)
                : result.OrderByDescending(item => item.StartAt);

            RecordedProgram[] materialized = result.ToArray();
            RecordedProgram[] page = materialized.Skip(query.Offset).Take(query.Limit).ToArray();
            return ValueTask.FromResult(new Page<RecordedProgram>(page, materialized.Length));
        }
    }

    public ValueTask<long> AddAsync(CreateRecordedCommand command, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (gate)
        {
            long id = ++nextId;
            records.Add(new RecordedProgram(
                Id: id,
                ChannelId: command.ChannelId,
                StartAt: command.StartAt,
                EndAt: command.EndAt,
                Name: command.Name,
                HalfWidthName: command.Name,
                IsRecording: false,
                IsEncoding: false,
                IsProtected: false,
                RuleId: command.RuleId,
                Description: command.Description,
                HalfWidthDescription: command.Description,
                Extended: command.Extended,
                HalfWidthExtended: command.Extended,
                Genre1: command.Genre1,
                SubGenre1: command.SubGenre1,
                Genre2: command.Genre2,
                SubGenre2: command.SubGenre2,
                Genre3: command.Genre3,
                SubGenre3: command.SubGenre3,
                Thumbnails: [],
                VideoFiles: []));

            return ValueTask.FromResult(id);
        }
    }

    /// <summary>保存先を持たない構成なので、空き容量を気にする自動削除の出番も無い。</summary>
    public ValueTask<long?> FindOldestUnprotectedAsync(string parentDirectoryPath, CancellationToken cancellationToken) =>
        ValueTask.FromResult<long?>(null);
}
