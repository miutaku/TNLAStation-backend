using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace TNLAStation.Api.Tests;

public sealed class ApiIntegrationTests : IDisposable
{
    private readonly WebApplicationFactory<Program> factory = new();
    private readonly HttpClient client;

    public ApiIntegrationTests()
    {
        client = factory.CreateClient();
    }

    [Fact]
    public async Task RecordedPostReturns201AndPersistsThroughRepository()
    {
        var request = new
        {
            channelId = 3_273_601_024L,
            startAt = 2_000_000_000_000L,
            endAt = 2_000_001_800_000L,
            name = "追加録画",
            description = (string?)null
        };

        using HttpResponseMessage createResponse = await client.PostAsJsonAsync("/api/recorded", request);
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        AssertNoCache(createResponse);
        using JsonDocument createDocument = await ReadJsonAsync(createResponse);
        long id = createDocument.RootElement.GetProperty("recordedId").GetInt64();
        Assert.True(id > 1);
        Assert.False(createDocument.RootElement.TryGetProperty("RecordedId", out _));

        using HttpResponseMessage listResponse =
            await client.GetAsync("/api/recorded?isHalfWidth=false&keyword=%E8%BF%BD%E5%8A%A0%E9%8C%B2%E7%94%BB");
        using JsonDocument listDocument = await ReadJsonAsync(listResponse);
        JsonElement item = listDocument.RootElement.GetProperty("records")[0];
        Assert.Equal(id, item.GetProperty("id").GetInt64());
        Assert.Equal("追加録画", item.GetProperty("name").GetString());
        Assert.False(item.TryGetProperty("description", out _));
    }

    [Fact]
    public async Task RecordedPostAcceptsEmptyNameLikeUpstreamSchema()
    {
        var request = new
        {
            channelId = 1,
            startAt = 2_000_000_000_000L,
            endAt = 2_000_001_800_000L,
            name = string.Empty
        };

        using HttpResponseMessage response = await client.PostAsJsonAsync("/api/recorded", request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task ReservePostReturns201AndPreservesMapperCompatibilityTypos()
    {
        var request = new
        {
            allowEndLack = false,
            timeSpecifiedOption = new
            {
                name = "追加予約",
                channelId = 2L,
                startAt = 2_000_000_000_000L,
                endAt = 2_000_001_800_000L
            },
            encodeOption = new
            {
                mode1 = "H.264",
                directory2 = "upstream-omits-this",
                mode3 = "H.265",
                directory3 = "third",
                isDeleteOriginalAfterEncode = false
            }
        };

        using HttpResponseMessage createResponse = await client.PostAsJsonAsync("/api/reserves", request);
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        AssertNoCache(createResponse);
        using JsonDocument createDocument = await ReadJsonAsync(createResponse);
        long id = createDocument.RootElement.GetProperty("reserveId").GetInt64();

        using HttpResponseMessage listResponse = await client.GetAsync("/api/reserves?isHalfWidth=false");
        using JsonDocument listDocument = await ReadJsonAsync(listResponse);
        JsonElement item = listDocument.RootElement.GetProperty("reserves")
            .EnumerateArray()
            .Single(element => element.GetProperty("id").GetInt64() == id);
        Assert.Equal("H.264", item.GetProperty("encodeMode1").GetString());
        Assert.False(item.TryGetProperty("encodeDirectory2", out _));
        Assert.Equal("third", item.GetProperty("encodeDirectory3").GetString());
        Assert.False(item.TryGetProperty("audioComponentType", out _));
    }

    [Fact]
    public async Task ReservePostWithoutProgramOrTimeOptionReturnsUpstream500Shape()
    {
        using HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/reserves",
            new { allowEndLack = false });

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        using JsonDocument document = await ReadJsonAsync(response);
        Assert.Equal(500, document.RootElement.GetProperty("code").GetInt32());
        Assert.Equal("Internal Server Error", document.RootElement.GetProperty("message").GetString());
        Assert.Equal("AddReservationOptionError", document.RootElement.GetProperty("errors").GetString());
        Assert.False(response.Headers.Contains("Cache-Control"));
    }

    public void Dispose()
    {
        client.Dispose();
        factory.Dispose();
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response) =>
        await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());

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
