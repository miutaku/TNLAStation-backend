using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace TNLAStation.Api.Tests;

public sealed class ApiContractTests : IDisposable
{
    private readonly WebApplicationFactory<Program> factory = new();
    private readonly HttpClient client;

    public ApiContractTests()
    {
        client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
    }

    [Fact]
    public async Task ConfigUsesExactRuntimePropertyNamesAndOmitsNulls()
    {
        using HttpResponseMessage response = await client.GetAsync("/api/config");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        AssertNoCache(response);
        using JsonDocument document = await ReadJsonAsync(response);
        JsonElement root = document.RootElement;

        Assert.Equal(JsonValueKind.Number, root.GetProperty("socketIOPort").ValueKind);
        Assert.Equal(8888, root.GetProperty("socketIOPort").GetInt32());
        Assert.False(root.TryGetProperty("isEnableLiveStream", out _));
        Assert.False(root.TryGetProperty("kodiHosts", out _));

        // stream 設定を持たない構成。EPGStation は 3 つのフラグを false のまま返し、
        // streamConfig は空オブジェクトとして出す。
        Assert.Equal(JsonValueKind.False, root.GetProperty("isEnableTSLiveStream").ValueKind);
        Assert.Equal(JsonValueKind.False, root.GetProperty("isEnableTSRecordedStream").ValueKind);
        Assert.Equal(JsonValueKind.False, root.GetProperty("isEnableEncodedRecordedStream").ValueKind);
        Assert.Empty(root.GetProperty("streamConfig").EnumerateObject());

        // チューナーに繋がっていない構成なので、放送波はどれも false。
        JsonElement broadcast = root.GetProperty("broadcast");
        Assert.False(broadcast.GetProperty("GR").GetBoolean());
        Assert.False(broadcast.GetProperty("BS").GetBoolean());
        Assert.False(broadcast.GetProperty("CS").GetBoolean());
        Assert.False(broadcast.GetProperty("SKY").GetBoolean());
        Assert.False(broadcast.TryGetProperty("gr", out _));

        JsonElement urlscheme = root.GetProperty("urlscheme");
        JsonElement m2ts = urlscheme.GetProperty("m2ts");
        Assert.False(urlscheme.TryGetProperty("m2Ts", out _));
        Assert.Equal(JsonValueKind.String, m2ts.GetProperty("ios").ValueKind);
        Assert.False(m2ts.TryGetProperty("mac", out _));
        Assert.False(m2ts.TryGetProperty("win", out _));
    }

    [Fact]
    public async Task RecordedUsesRequiredEnvelopeAndUpstreamRawExtendedBehavior()
    {
        using HttpResponseMessage fullWidthResponse = await client.GetAsync("/api/recorded?isHalfWidth=false");
        Assert.Equal(HttpStatusCode.OK, fullWidthResponse.StatusCode);
        AssertNoCache(fullWidthResponse);
        using JsonDocument fullWidthDocument = await ReadJsonAsync(fullWidthResponse);
        JsonElement fullWidthRoot = fullWidthDocument.RootElement;
        JsonElement fullWidthRecord = fullWidthRoot.GetProperty("records")[0];

        Assert.Equal(1, fullWidthRoot.GetProperty("total").GetInt32());
        Assert.Equal(JsonValueKind.Number, fullWidthRecord.GetProperty("id").ValueKind);
        Assert.Equal(JsonValueKind.Number, fullWidthRecord.GetProperty("channelId").ValueKind);
        Assert.False(fullWidthRecord.GetProperty("isRecording").GetBoolean());
        Assert.False(fullWidthRecord.GetProperty("isEncoding").GetBoolean());
        Assert.False(fullWidthRecord.GetProperty("isProtected").GetBoolean());
        Assert.False(fullWidthRecord.TryGetProperty("rawExtended", out _));
        Assert.False(fullWidthRecord.TryGetProperty("dropLog", out _));
        Assert.False(fullWidthRecord.TryGetProperty("dropLogFile", out _));
        Assert.False(fullWidthRecord.TryGetProperty("ruleId", out _));

        using HttpResponseMessage halfWidthResponse = await client.GetAsync("/api/recorded?isHalfWidth=true");
        using JsonDocument halfWidthDocument = await ReadJsonAsync(halfWidthResponse);
        JsonElement halfWidthRecord = halfWidthDocument.RootElement.GetProperty("records")[0];
        Assert.Equal(JsonValueKind.Object, halfWidthRecord.GetProperty("rawExtended").ValueKind);
    }

    [Fact]
    public async Task ReservesUsesRequiredEnvelopeAndDistinctRawExtendedBehavior()
    {
        using HttpResponseMessage fullWidthResponse = await client.GetAsync("/api/reserves?isHalfWidth=false");
        Assert.Equal(HttpStatusCode.OK, fullWidthResponse.StatusCode);
        AssertNoCache(fullWidthResponse);
        using JsonDocument fullWidthDocument = await ReadJsonAsync(fullWidthResponse);
        JsonElement fullWidthRoot = fullWidthDocument.RootElement;
        JsonElement fullWidthReserve = fullWidthRoot.GetProperty("reserves")[0];

        Assert.Equal(1, fullWidthRoot.GetProperty("total").GetInt32());
        Assert.Equal(JsonValueKind.Number, fullWidthReserve.GetProperty("id").ValueKind);
        Assert.False(fullWidthReserve.GetProperty("isSkip").GetBoolean());
        Assert.False(fullWidthReserve.GetProperty("isConflict").GetBoolean());
        Assert.False(fullWidthReserve.GetProperty("isOverlap").GetBoolean());
        Assert.False(fullWidthReserve.GetProperty("allowEndLack").GetBoolean());
        Assert.True(fullWidthReserve.GetProperty("isTimeSpecified").GetBoolean());
        Assert.Equal(JsonValueKind.Object, fullWidthReserve.GetProperty("rawExtended").ValueKind);

        using HttpResponseMessage halfWidthResponse = await client.GetAsync("/api/reserves?isHalfWidth=true");
        using JsonDocument halfWidthDocument = await ReadJsonAsync(halfWidthResponse);
        JsonElement halfWidthReserve = halfWidthDocument.RootElement.GetProperty("reserves")[0];
        Assert.False(halfWidthReserve.TryGetProperty("rawExtended", out _));
    }

    [Fact]
    public async Task RecordedAndReserveListsAcceptSharedSearchFields()
    {
        using HttpResponseMessage recordedResponse = await client.GetAsync(
            "/api/recorded?isHalfWidth=false&keyword=%E3%83%95%E3%82%A7%E3%83%BC%E3%82%BA1&ruleId=0&channelId=1&genre=0");
        Assert.Equal(HttpStatusCode.OK, recordedResponse.StatusCode);
        using JsonDocument recordedDocument = await ReadJsonAsync(recordedResponse);
        Assert.Equal(1, recordedDocument.RootElement.GetProperty("total").GetInt32());

        using HttpResponseMessage reserveResponse = await client.GetAsync(
            "/api/reserves?isHalfWidth=false&keyword=%E3%83%A2%E3%83%83%E3%82%AF%E4%BA%88%E7%B4%84&ruleId=0&channelId=1");
        Assert.Equal(HttpStatusCode.OK, reserveResponse.StatusCode);
        using JsonDocument reserveDocument = await ReadJsonAsync(reserveResponse);
        Assert.Equal(1, reserveDocument.RootElement.GetProperty("total").GetInt32());

        using HttpResponseMessage noMatchResponse = await client.GetAsync(
            "/api/reserves?isHalfWidth=false&keyword=missing");
        using JsonDocument noMatchDocument = await ReadJsonAsync(noMatchResponse);
        Assert.Equal(0, noMatchDocument.RootElement.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task VersionReturnsReferenceVersionWithExactKey()
    {
        using HttpResponseMessage response = await client.GetAsync("/api/version");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        AssertNoCache(response);
        using JsonDocument document = await ReadJsonAsync(response);
        Assert.Equal("2.10.0", document.RootElement.GetProperty("version").GetString());
        Assert.Single(document.RootElement.EnumerateObject());
    }

    [Fact]
    public async Task OpenApiIsPublishedOnlyAtCompatibilityRoutesAndDocumentsDefaults()
    {
        using HttpResponseMessage docsResponse = await client.GetAsync("/api/docs");
        Assert.Equal(HttpStatusCode.OK, docsResponse.StatusCode);
        using JsonDocument document = await ReadJsonAsync(docsResponse);
        JsonElement paths = document.RootElement.GetProperty("paths");

        Assert.True(paths.GetProperty("/config").TryGetProperty("get", out _));
        Assert.True(paths.GetProperty("/recorded").TryGetProperty("get", out JsonElement recordedGet));
        Assert.True(paths.GetProperty("/recorded").TryGetProperty("post", out JsonElement recordedPost));
        Assert.True(paths.GetProperty("/reserves").TryGetProperty("get", out _));
        Assert.True(paths.GetProperty("/reserves").TryGetProperty("post", out JsonElement reservesPost));
        Assert.True(paths.GetProperty("/version").TryGetProperty("get", out _));
        Assert.True(recordedGet.GetProperty("responses").TryGetProperty("200", out _));
        Assert.True(recordedPost.GetProperty("responses").TryGetProperty("201", out _));
        Assert.True(reservesPost.GetProperty("responses").TryGetProperty("201", out _));

        JsonElement parameters = document.RootElement.GetProperty("components").GetProperty("parameters");
        JsonElement isHalfWidth = parameters.GetProperty("IsHalfWidth");
        JsonElement offset = parameters.GetProperty("Offset");
        JsonElement limit = parameters.GetProperty("Limit");
        Assert.True(isHalfWidth.GetProperty("required").GetBoolean());
        Assert.Equal(0, offset.GetProperty("schema").GetProperty("default").GetInt32());
        Assert.Equal(24, limit.GetProperty("schema").GetProperty("default").GetInt32());


        using HttpResponseMessage defaultOpenApiResponse = await client.GetAsync("/openapi/v1.json");
        Assert.Equal(HttpStatusCode.NotFound, defaultOpenApiResponse.StatusCode);
        using HttpResponseMessage healthResponse = await client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.NotFound, healthResponse.StatusCode);
    }

    [Fact]
    public async Task RequiredHalfWidthQueryIsValidated()
    {
        using HttpResponseMessage recorded = await client.GetAsync("/api/recorded");
        using HttpResponseMessage reserves = await client.GetAsync("/api/reserves");

        Assert.Equal(HttpStatusCode.BadRequest, recorded.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, reserves.StatusCode);
        Assert.Equal(
            """{"status":400,"errors":[{"message":"must have required property 'isHalfWidth'"}]}""",
            await recorded.Content.ReadAsStringAsync());
        Assert.Equal(
            """{"status":400,"errors":[{"message":"must have required property 'isHalfWidth'"}]}""",
            await reserves.Content.ReadAsStringAsync());
    }

    public void Dispose()
    {
        client.Dispose();
        factory.Dispose();
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response)
    {
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        return await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
    }

    private static void AssertNoCache(HttpResponseMessage response)
    {
        Assert.Equal(
            "private, no-cache, no-store, must-revalidate",
            response.Headers.NonValidated["Cache-Control"].ToString());
        Assert.Equal("-1", Assert.Single(GetHeaderValues(response, "Expires")));
        Assert.Equal("no-cache", Assert.Single(GetHeaderValues(response, "Pragma")));
    }

    private static IEnumerable<string> GetHeaderValues(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out IEnumerable<string>? values)
            ? values
            : response.Content.Headers.GetValues(name);

}
