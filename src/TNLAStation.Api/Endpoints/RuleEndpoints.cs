using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using TNLAStation.Api.Contracts;
using TNLAStation.Application.Abstractions;
using TNLAStation.Application.Models;
using TNLAStation.Infrastructure.Configuration;

namespace TNLAStation.Api.Endpoints;

internal static class RuleEndpoints
{
    public static IEndpointRouteBuilder MapRuleEndpoints(this IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder rules = endpoints.MapGroup("/api/rules");

        rules.MapGet("/", GetRulesAsync)
            .WithName("GetRules")
            .WithSummary("ルール情報取得")
            .WithTags("rules")
            .Produces<RulesResponse>();

        rules.MapPost("/", AddRuleAsync)
            .WithName("AddRule")
            .WithSummary("ルール追加")
            .WithTags("rules")
            .Accepts<AddRuleRequest>("application/json")
            .Produces<AddedRuleResponse>(StatusCodes.Status201Created);

        // 上流は POST /rules/keyword にもルール追加を割り当てている。意図した設計には
        // 見えないが、そこへ投げる利用側がいる以上、同じように受ける。
        rules.MapPost("/keyword", AddRuleAsync)
            .WithName("AddRuleByKeywordPath")
            .WithSummary("ルール追加")
            .WithTags("rules")
            .Accepts<AddRuleRequest>("application/json")
            .Produces<AddedRuleResponse>(StatusCodes.Status201Created);

        rules.MapGet("/keyword", SearchRuleKeywordsAsync)
            .WithName("SearchRuleKeywords")
            .WithSummary("ルールをキーワード検索")
            .WithTags("rules")
            .Produces<RuleKeywordInfoResponse>();

        rules.MapGet("/{ruleId}", GetRuleAsync)
            .WithName("GetRule")
            .WithSummary("ルール取得")
            .WithTags("rules")
            .Produces<RuleResponse>()
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound);

        rules.MapPut("/{ruleId}", UpdateRuleAsync)
            .WithName("UpdateRule")
            .WithSummary("ルール更新")
            .WithTags("rules")
            .Accepts<AddRuleRequest>("application/json")
            .Produces<ResultCodeResponse>();

        rules.MapDelete("/{ruleId}", DeleteRuleAsync)
            .WithName("DeleteRule")
            .WithSummary("ルール削除")
            .WithTags("rules")
            .Produces<ResultCodeResponse>();

        rules.MapPut("/{ruleId}/enable", EnableRuleAsync)
            .WithName("EnableRule")
            .WithSummary("ルール有効化")
            .WithTags("rules")
            .Produces<ResultCodeResponse>();

        rules.MapPut("/{ruleId}/disable", DisableRuleAsync)
            .WithName("DisableRule")
            .WithSummary("ルール無効化")
            .WithTags("rules")
            .Produces<ResultCodeResponse>();

        return endpoints;
    }

    private static async Task<IResult> GetRulesAsync(
        IRuleRepository repository,
        [FromQuery] int? offset,
        [FromQuery] int? limit,
        [FromQuery] string? type,
        [FromQuery] string? keyword,
        CancellationToken cancellationToken)
    {
        Page<RecordingRule> page = await repository.ListAsync(
            new RuleQuery(offset, limit, keyword, type),
            cancellationToken);

        return Results.Ok(new RulesResponse(
            page.Items.Select(rule => rule.ToResponse(includeReservesCount: type is not null)).ToArray(),
            page.Total));
    }

    private static async Task<IResult> SearchRuleKeywordsAsync(
        IRuleRepository repository,
        [FromQuery] int? offset,
        [FromQuery] int? limit,
        [FromQuery] string? keyword,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<RuleKeywordItem> items = await repository.ListKeywordsAsync(
            new RuleQuery(offset, limit, keyword),
            cancellationToken);

        return Results.Ok(new RuleKeywordInfoResponse(
            items.Select(item => new RuleKeywordItemResponse(item.Id, item.Keyword)).ToArray()));
    }

    private static async Task<IResult> GetRuleAsync(
        long ruleId,
        IRuleRepository repository,
        CancellationToken cancellationToken)
    {
        RecordingRule? rule = await repository.GetAsync(ruleId, cancellationToken);
        return rule is null
            ? Results.Json(
                new ErrorResponse(StatusCodes.Status404NotFound, "Rule is not Found"),
                statusCode: StatusCodes.Status404NotFound)
            : Results.Ok(rule.ToResponse(includeReservesCount: false));
    }

    private static async Task<IResult> AddRuleAsync(
        AddRuleRequest request,
        IRuleRepository repository,
        IOptions<EncodeOptions> encodeOptions,
        CancellationToken cancellationToken)
    {
        RecordingRule rule = request.ToRule();
        ValidateRule(rule, encodeOptions.Value, "AddRuleError");

        long ruleId = await repository.AddAsync(rule, cancellationToken);
        return Results.Json(new AddedRuleResponse(ruleId), statusCode: StatusCodes.Status201Created);
    }

    private static async Task<IResult> UpdateRuleAsync(
        long ruleId,
        AddRuleRequest request,
        IRuleRepository repository,
        IOptions<EncodeOptions> encodeOptions,
        CancellationToken cancellationToken)
    {
        RecordingRule rule = request.ToRule(ruleId);
        ValidateRule(rule, encodeOptions.Value, "UpdateRuleError");

        await repository.UpdateAsync(rule, cancellationToken);
        return Ok();
    }

    /// <summary>
    /// 上流 (ReserveOptionChecker.checkRuleOption) が追加・更新時にかけている検査。落ちたら
    /// AddRuleError/UpdateRuleError を投げて汎用の 500 になる。
    /// </summary>
    private static void ValidateRule(RecordingRule rule, EncodeOptions encodeOptions, string errorMessage) =>
        RuleValidationPolicy.Validate(
            rule,
            [.. encodeOptions.Modes.Select(mode => mode.Name)],
            encodeOptions.Modes.Count > 0,
            errorMessage);

    private static async Task<IResult> DeleteRuleAsync(
        long ruleId,
        IRuleRepository repository,
        CancellationToken cancellationToken)
    {
        await repository.DeleteAsync(ruleId, cancellationToken);
        return Ok();
    }

    private static async Task<IResult> EnableRuleAsync(
        long ruleId,
        IRuleRepository repository,
        CancellationToken cancellationToken)
    {
        if (await repository.GetAsync(ruleId, cancellationToken) is null)
        {
            throw new InvalidOperationException("RuleIsNull");
        }

        await repository.SetEnabledAsync(ruleId, isEnabled: true, cancellationToken);
        return Ok();
    }

    private static async Task<IResult> DisableRuleAsync(
        long ruleId,
        IRuleRepository repository,
        CancellationToken cancellationToken)
    {
        if (await repository.GetAsync(ruleId, cancellationToken) is null)
        {
            throw new InvalidOperationException("RuleIsNull");
        }

        await repository.SetEnabledAsync(ruleId, isEnabled: false, cancellationToken);
        return Ok();
    }

    /// <summary>
    /// EPGStation answers its rule mutations with the status code in the body as well.
    /// </summary>
    private static IResult Ok() => Results.Ok(new ResultCodeResponse(StatusCodes.Status200OK));
}
