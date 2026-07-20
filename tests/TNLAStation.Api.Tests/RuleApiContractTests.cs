using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.Testing;

namespace TNLAStation.Api.Tests;

/// <summary>
/// Fixes the rules surface against EPGStation v2.10.0, including the parts a naive implementation
/// would "fix": mutations answer with the status code in the body, a missing rule is a 500 carrying
/// "RuleIsNull" for updates but a success for deletes, and updateCnt never leaves the server.
/// </summary>
public sealed class RuleApiContractTests : IDisposable
{
    /// <summary>
    /// EPGStation clients send the broadcast flags upper case, so the requests must not be
    /// camel-cased on the way out.
    /// </summary>
    private static readonly JsonSerializerOptions RequestOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = null,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly WebApplicationFactory<Program> factory = new();
    private readonly HttpClient client;

    public RuleApiContractTests()
    {
        client = factory.CreateClient();
    }

    [Fact]
    public async Task AddedRuleRoundTripsThroughGetWithoutInternalFields()
    {
        long ruleId = await AddRuleAsync(CreateRuleRequest("アニメ"));

        using HttpResponseMessage response = await client.GetAsync($"/api/rules/{ruleId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        AssertNoCache(response);
        using JsonDocument document = await ReadJsonAsync(response);
        JsonElement rule = document.RootElement;

        Assert.Equal(ruleId, rule.GetProperty("id").GetInt64());
        Assert.False(rule.GetProperty("isTimeSpecification").GetBoolean());
        Assert.False(rule.TryGetProperty("updateCnt", out _));
        Assert.False(rule.TryGetProperty("reservesCnt", out _));

        JsonElement search = rule.GetProperty("searchOption");
        Assert.Equal("アニメ", search.GetProperty("keyword").GetString());
        Assert.True(search.GetProperty("GR").GetBoolean());
        Assert.False(search.GetProperty("BS").GetBoolean());
        Assert.True(search.GetProperty("name").GetBoolean());
        Assert.False(search.GetProperty("keyCS").GetBoolean());
        Assert.False(search.TryGetProperty("ignoreKeyword", out _));
        Assert.False(search.TryGetProperty("channelIds", out _));
        Assert.False(search.TryGetProperty("gr", out _));

        JsonElement reserve = rule.GetProperty("reserveOption");
        Assert.True(reserve.GetProperty("enable").GetBoolean());
        Assert.False(reserve.GetProperty("allowEndLack").GetBoolean());
        Assert.False(reserve.GetProperty("avoidDuplicate").GetBoolean());
        Assert.False(reserve.TryGetProperty("periodToAvoidDuplicate", out _));

        // Neither option was sent, so neither is echoed back.
        Assert.False(rule.TryGetProperty("saveOption", out _));
        Assert.False(rule.TryGetProperty("encodeOption", out _));
    }

    [Fact]
    public async Task RuleListPagesInIdOrderAndCountsTheWholeMatch()
    {
        long first = await AddRuleAsync(CreateRuleRequest("ドラマ 再放送"));
        long second = await AddRuleAsync(CreateRuleRequest("ドラマ 特集"));

        using HttpResponseMessage response = await client.GetAsync("/api/rules?offset=1&limit=1&keyword=ドラマ");

        using JsonDocument document = await ReadJsonAsync(response);
        Assert.Equal(2, document.RootElement.GetProperty("total").GetInt32());
        JsonElement rule = Assert.Single(document.RootElement.GetProperty("rules").EnumerateArray().ToArray());
        Assert.Equal(second, rule.GetProperty("id").GetInt64());
        Assert.True(second > first);
    }

    [Fact]
    public async Task RuleKeywordSearchRequiresEveryTermAndNormalizesWidth()
    {
        long ruleId = await AddRuleAsync(CreateRuleRequest("ＮＨＫ　ニュース"));
        await AddRuleAsync(CreateRuleRequest("映画"));

        using HttpResponseMessage matching = await client.GetAsync("/api/rules/keyword?keyword=NHK ニュース");
        using JsonDocument matchingDocument = await ReadJsonAsync(matching);
        JsonElement item = Assert.Single(matchingDocument.RootElement.GetProperty("items").EnumerateArray().ToArray());
        Assert.Equal(ruleId, item.GetProperty("id").GetInt64());
        Assert.Equal("ＮＨＫ　ニュース", item.GetProperty("keyword").GetString());

        using HttpResponseMessage partial = await client.GetAsync("/api/rules/keyword?keyword=NHK 天気");
        using JsonDocument partialDocument = await ReadJsonAsync(partial);
        Assert.Empty(partialDocument.RootElement.GetProperty("items").EnumerateArray().ToArray());
    }

    [Fact]
    public async Task RuleListReportsReserveCountOnlyWhenATypeIsRequested()
    {
        await AddRuleAsync(CreateRuleRequest("カウント"));

        using HttpResponseMessage withType = await client.GetAsync("/api/rules?type=normal&keyword=カウント");
        using JsonDocument withTypeDocument = await ReadJsonAsync(withType);
        Assert.Equal(0, withTypeDocument.RootElement.GetProperty("rules")[0].GetProperty("reservesCnt").GetInt32());

        using HttpResponseMessage withoutType = await client.GetAsync("/api/rules?keyword=カウント");
        using JsonDocument withoutTypeDocument = await ReadJsonAsync(withoutType);
        Assert.False(withoutTypeDocument.RootElement.GetProperty("rules")[0].TryGetProperty("reservesCnt", out _));
    }

    [Fact]
    public async Task RuleMutationsAnswerWithTheStatusCodeInTheBody()
    {
        long ruleId = await AddRuleAsync(CreateRuleRequest("有効化"));

        using HttpResponseMessage disable = await client.PutAsync($"/api/rules/{ruleId}/disable", content: null);
        Assert.Equal(HttpStatusCode.OK, disable.StatusCode);
        AssertNoCache(disable);
        using JsonDocument disableDocument = await ReadJsonAsync(disable);
        Assert.Equal(200, disableDocument.RootElement.GetProperty("code").GetInt32());

        using HttpResponseMessage disabled = await client.GetAsync($"/api/rules/{ruleId}");
        using JsonDocument disabledDocument = await ReadJsonAsync(disabled);
        Assert.False(disabledDocument.RootElement.GetProperty("reserveOption").GetProperty("enable").GetBoolean());

        using HttpResponseMessage enable = await client.PutAsync($"/api/rules/{ruleId}/enable", content: null);
        Assert.Equal(HttpStatusCode.OK, enable.StatusCode);

        using HttpResponseMessage enabled = await client.GetAsync($"/api/rules/{ruleId}");
        using JsonDocument enabledDocument = await ReadJsonAsync(enabled);
        Assert.True(enabledDocument.RootElement.GetProperty("reserveOption").GetProperty("enable").GetBoolean());
    }

    [Fact]
    public async Task RuleUpdateReplacesTheStoredOptions()
    {
        long ruleId = await AddRuleAsync(CreateRuleRequest("更新前"));

        var update = new
        {
            isTimeSpecification = true,
            searchOption = new { keyword = "更新後", name = true, BS = true },
            reserveOption = new { enable = false, allowEndLack = true, avoidDuplicate = true, periodToAvoidDuplicate = 6 },
            saveOption = new { directory = "anime" },
            encodeOption = new { mode1 = "H.264", isDeleteOriginalAfterEncode = true }
        };
        using HttpResponseMessage response = await client.PutAsJsonAsync($"/api/rules/{ruleId}", update, RequestOptions);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using HttpResponseMessage updated = await client.GetAsync($"/api/rules/{ruleId}");
        using JsonDocument document = await ReadJsonAsync(updated);
        JsonElement rule = document.RootElement;
        Assert.True(rule.GetProperty("isTimeSpecification").GetBoolean());
        Assert.Equal("更新後", rule.GetProperty("searchOption").GetProperty("keyword").GetString());
        Assert.True(rule.GetProperty("searchOption").GetProperty("BS").GetBoolean());
        Assert.False(rule.GetProperty("searchOption").GetProperty("GR").GetBoolean());
        Assert.Equal(6, rule.GetProperty("reserveOption").GetProperty("periodToAvoidDuplicate").GetInt32());
        Assert.Equal("anime", rule.GetProperty("saveOption").GetProperty("directory").GetString());
        Assert.False(rule.GetProperty("saveOption").TryGetProperty("parentDirectoryName", out _));
        Assert.True(rule.GetProperty("encodeOption").GetProperty("isDeleteOriginalAfterEncode").GetBoolean());
        Assert.Equal("H.264", rule.GetProperty("encodeOption").GetProperty("mode1").GetString());
    }

    [Fact]
    public async Task DeletedRuleIsGoneAndDeletingItAgainStillSucceeds()
    {
        long ruleId = await AddRuleAsync(CreateRuleRequest("削除"));

        using HttpResponseMessage deleted = await client.DeleteAsync($"/api/rules/{ruleId}");
        Assert.Equal(HttpStatusCode.OK, deleted.StatusCode);

        using HttpResponseMessage missing = await client.GetAsync($"/api/rules/{ruleId}");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        using JsonDocument missingDocument = await ReadJsonAsync(missing);
        Assert.Equal("Rule is not Found", missingDocument.RootElement.GetProperty("message").GetString());
        Assert.False(missing.Headers.Contains("Cache-Control"));

        using HttpResponseMessage again = await client.DeleteAsync($"/api/rules/{ruleId}");
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
    }

    [Fact]
    public async Task TogglingOrUpdatingAMissingRuleReturnsTheUpstream500Shape()
    {
        using HttpResponseMessage enable = await client.PutAsync("/api/rules/9999/enable", content: null);
        Assert.Equal(HttpStatusCode.InternalServerError, enable.StatusCode);
        using JsonDocument document = await ReadJsonAsync(enable);
        Assert.Equal(500, document.RootElement.GetProperty("code").GetInt32());
        Assert.Equal("Internal Server Error", document.RootElement.GetProperty("message").GetString());
        Assert.Equal("RuleIsNull", document.RootElement.GetProperty("errors").GetString());

        using HttpResponseMessage update = await client.PutAsJsonAsync("/api/rules/9999", CreateRuleRequest("欠番"), RequestOptions);
        Assert.Equal(HttpStatusCode.InternalServerError, update.StatusCode);
    }

    public void Dispose()
    {
        client.Dispose();
        factory.Dispose();
    }

    private async Task<long> AddRuleAsync(object request)
    {
        using HttpResponseMessage response = await client.PostAsJsonAsync("/api/rules", request, RequestOptions);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using JsonDocument document = await ReadJsonAsync(response);
        return document.RootElement.GetProperty("ruleId").GetInt64();
    }

    private static object CreateRuleRequest(string keyword) => new
    {
        isTimeSpecification = false,
        searchOption = new { keyword, name = true, GR = true },
        reserveOption = new { enable = true, allowEndLack = false, avoidDuplicate = false }
    };

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response) =>
        await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());

    private static void AssertNoCache(HttpResponseMessage response) =>
        Assert.Equal(
            "private, no-cache, no-store, must-revalidate",
            response.Headers.NonValidated["Cache-Control"].ToString());
}
