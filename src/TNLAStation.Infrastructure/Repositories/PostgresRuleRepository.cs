using Microsoft.EntityFrameworkCore;
using TNLAStation.Application.Abstractions;
using TNLAStation.Application.Models;
using TNLAStation.Infrastructure.Persistence;

namespace TNLAStation.Infrastructure.Repositories;

public sealed class PostgresRuleRepository(IDbContextFactory<EpgDbContext> contextFactory) : IRuleRepository
{
    public async ValueTask<Page<RecordingRule>> ListAsync(
        RuleQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        await using EpgDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);
        IQueryable<RuleEntity> rules = ApplyKeyword(context.Rules.AsNoTracking(), query.Keyword)
            .OrderBy(rule => rule.Id);
        int total = await rules.CountAsync(cancellationToken);
        RuleEntity[] items = await ApplyPaging(rules, query).ToArrayAsync(cancellationToken);

        return new Page<RecordingRule>(
            items.Select(item => item.ToDomain()).ToArray(),
            total);
    }

    public async ValueTask<IReadOnlyList<RuleKeywordItem>> ListKeywordsAsync(
        RuleQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        await using EpgDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);
        IQueryable<RuleEntity> rules = ApplyKeyword(context.Rules.AsNoTracking(), query.Keyword)
            .OrderBy(rule => rule.Id);

        return await ApplyPaging(rules, query)
            .Select(rule => new RuleKeywordItem(rule.Id, rule.Keyword ?? string.Empty))
            .ToArrayAsync(cancellationToken);
    }

    public async ValueTask<RecordingRule?> GetAsync(long ruleId, CancellationToken cancellationToken)
    {
        await using EpgDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);
        RuleEntity? entity = await context.Rules.AsNoTracking()
            .SingleOrDefaultAsync(rule => rule.Id == ruleId, cancellationToken);
        return entity?.ToDomain();
    }

    public async ValueTask<long> AddAsync(RecordingRule rule, CancellationToken cancellationToken)
    {
        await using EpgDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var entity = new RuleEntity();
        entity.Apply(rule);
        context.Rules.Add(entity);
        await context.SaveChangesAsync(cancellationToken);
        return entity.Id;
    }

    public async ValueTask UpdateAsync(RecordingRule rule, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(rule);

        await using EpgDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);
        RuleEntity entity = await FindForWriteAsync(context, rule.Id, cancellationToken);
        entity.Apply(rule);
        entity.UpdateCount++;
        await context.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// 上流の enable/disable は存在チェックをしない (無ければ何も起きず 200 のまま) ので、
    /// update と違って見つからなくても例外にしない。
    /// </summary>
    public async ValueTask SetEnabledAsync(long ruleId, bool isEnabled, CancellationToken cancellationToken)
    {
        await using EpgDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);
        RuleEntity? entity = await context.Rules.SingleOrDefaultAsync(rule => rule.Id == ruleId, cancellationToken);
        if (entity is null || entity.Enable == isEnabled)
        {
            return;
        }

        entity.Enable = isEnabled;
        entity.UpdateCount++;
        await context.SaveChangesAsync(cancellationToken);
    }

    public async ValueTask DeleteAsync(long ruleId, CancellationToken cancellationToken)
    {
        await using EpgDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await context.Rules.Where(rule => rule.Id == ruleId).ExecuteDeleteAsync(cancellationToken);
    }

    private static async Task<RuleEntity> FindForWriteAsync(
        EpgDbContext context,
        long ruleId,
        CancellationToken cancellationToken) =>
        await context.Rules.SingleOrDefaultAsync(rule => rule.Id == ruleId, cancellationToken)
            ?? throw new InvalidOperationException(RuleQueryPolicy.MissingRuleError);

    private static IQueryable<RuleEntity> ApplyKeyword(IQueryable<RuleEntity> rules, string? keyword)
    {
        foreach (string term in RuleQueryPolicy.SplitKeyword(keyword))
        {
            string pattern = $"%{term}%";
            rules = rules.Where(rule => EF.Functions.ILike(rule.HalfWidthKeyword!, pattern));
        }

        return rules;
    }

    private static IQueryable<RuleEntity> ApplyPaging(IQueryable<RuleEntity> rules, RuleQuery query)
    {
        if (query.Offset is > 0)
        {
            rules = rules.Skip(query.Offset.Value);
        }

        return query.Limit is not null ? rules.Take(query.Limit.Value) : rules;
    }
}
