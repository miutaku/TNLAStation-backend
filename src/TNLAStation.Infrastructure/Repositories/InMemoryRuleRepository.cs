using TNLAStation.Application.Abstractions;
using TNLAStation.Application.Models;

namespace TNLAStation.Infrastructure.Repositories;

/// <summary>
/// Rule store used when no PostgreSQL connection is configured. It keeps the same ordering,
/// keyword matching and error behavior as the PostgreSQL store so the HTTP contract does not
/// depend on which store is active.
/// </summary>
public sealed class InMemoryRuleRepository : IRuleRepository
{
    private readonly object gate = new();
    private readonly SortedDictionary<long, RecordingRule> rules = [];
    private long nextId;

    public ValueTask<Page<RecordingRule>> ListAsync(RuleQuery query, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            RecordingRule[] matched = Match(query).ToArray();
            return ValueTask.FromResult(new Page<RecordingRule>(
                ApplyPaging(matched, query).ToArray(),
                matched.Length));
        }
    }

    public ValueTask<IReadOnlyList<RuleKeywordItem>> ListKeywordsAsync(
        RuleQuery query,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            return ValueTask.FromResult<IReadOnlyList<RuleKeywordItem>>(
                ApplyPaging(Match(query), query)
                    .Select(rule => new RuleKeywordItem(rule.Id, rule.SearchOption.Keyword ?? string.Empty))
                    .ToArray());
        }
    }

    public ValueTask<RecordingRule?> GetAsync(long ruleId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            rules.TryGetValue(ruleId, out RecordingRule? rule);
            return ValueTask.FromResult(rule);
        }
    }

    public ValueTask<long> AddAsync(RecordingRule rule, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(rule);
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            long id = ++nextId;
            rules[id] = rule with { Id = id, UpdateCount = 0 };
            return ValueTask.FromResult(id);
        }
    }

    public ValueTask UpdateAsync(RecordingRule rule, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(rule);
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            RecordingRule current = Find(rule.Id);
            rules[rule.Id] = rule with { UpdateCount = current.UpdateCount + 1 };
            return ValueTask.CompletedTask;
        }
    }

    /// <summary>
    /// 上流の enable/disable は存在チェックをしない (無ければ何も起きず 200 のまま) ので、
    /// ここも見つからなければ静かに無視する。
    /// </summary>
    public ValueTask SetEnabledAsync(long ruleId, bool isEnabled, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            if (!rules.TryGetValue(ruleId, out RecordingRule? current) || current.ReserveOption.Enable == isEnabled)
            {
                return ValueTask.CompletedTask;
            }

            rules[ruleId] = current with
            {
                ReserveOption = current.ReserveOption with { Enable = isEnabled },
                UpdateCount = current.UpdateCount + 1
            };
            return ValueTask.CompletedTask;
        }
    }

    public ValueTask DeleteAsync(long ruleId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            rules.Remove(ruleId);
            return ValueTask.CompletedTask;
        }
    }

    private RecordingRule Find(long ruleId) =>
        rules.TryGetValue(ruleId, out RecordingRule? rule)
            ? rule
            : throw new InvalidOperationException(RuleQueryPolicy.MissingRuleError);

    private IEnumerable<RecordingRule> Match(RuleQuery query) =>
        rules.Values.Where(rule => RuleQueryPolicy.MatchesKeyword(rule.SearchOption.Keyword, query.Keyword));

    private static IEnumerable<RecordingRule> ApplyPaging(IEnumerable<RecordingRule> source, RuleQuery query)
    {
        if (query.Offset is > 0)
        {
            source = source.Skip(query.Offset.Value);
        }

        return query.Limit is not null ? source.Take(query.Limit.Value) : source;
    }
}
