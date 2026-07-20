using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace TNLAStation.Api.Tests;

/// <summary>
/// 空になりうる一覧の応答。録画していない、エンコード待ちがない、tag を作っていないのは
/// どれも正常な状態なので、404 ではなく空の配列と 0 件を返す。
/// </summary>
public sealed class CollectionApiContractTests : IDisposable
{
    private readonly WebApplicationFactory<Program> factory = new();
    private readonly HttpClient client;

    public CollectionApiContractTests()
    {
        client = factory.CreateClient();
    }

    [Fact]
    public async Task NothingRecordingIsAnEmptyListRatherThanAMissingResource()
    {
        using HttpResponseMessage response = await client.GetAsync("/api/recording?isHalfWidth=false&offset=0&limit=24");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        AssertNoCache(response);
        using JsonDocument document = await ReadJsonAsync(response);
        Assert.Empty(document.RootElement.GetProperty("records").EnumerateArray().ToArray());
        Assert.Equal(0, document.RootElement.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task RecordingRejectsANegativeWindowLikeTheRecordedList()
    {
        using HttpResponseMessage response = await client.GetAsync("/api/recording?isHalfWidth=false&offset=-1");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AnIdleEncodeQueueKeepsBothArrays()
    {
        using HttpResponseMessage response = await client.GetAsync("/api/encode?isHalfWidth=false");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using JsonDocument document = await ReadJsonAsync(response);
        Assert.Empty(document.RootElement.GetProperty("runningItems").EnumerateArray().ToArray());
        Assert.Empty(document.RootElement.GetProperty("waitItems").EnumerateArray().ToArray());
    }

    [Fact]
    public async Task NoStreamIsAnEmptyItemList()
    {
        using HttpResponseMessage response = await client.GetAsync("/api/streams?isHalfWidth=false");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using JsonDocument document = await ReadJsonAsync(response);
        Assert.Empty(document.RootElement.GetProperty("items").EnumerateArray().ToArray());
    }

    [Fact]
    public async Task NoTagIsAnEmptyListWithATotal()
    {
        using HttpResponseMessage response = await client.GetAsync("/api/tags");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using JsonDocument document = await ReadJsonAsync(response);
        Assert.Empty(document.RootElement.GetProperty("tags").EnumerateArray().ToArray());
        Assert.Equal(0, document.RootElement.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task ReserveCountsSplitEveryReserveIntoExactlyOneBucket()
    {
        using HttpResponseMessage counts = await client.GetAsync("/api/reserves/cnts");
        Assert.Equal(HttpStatusCode.OK, counts.StatusCode);
        using JsonDocument document = await ReadJsonAsync(counts);

        int total = document.RootElement.GetProperty("normal").GetInt32() +
            document.RootElement.GetProperty("conflicts").GetInt32() +
            document.RootElement.GetProperty("skips").GetInt32() +
            document.RootElement.GetProperty("overlaps").GetInt32();

        using HttpResponseMessage reserves = await client.GetAsync("/api/reserves?isHalfWidth=false&type=all&limit=1000");
        using JsonDocument reservesDocument = await ReadJsonAsync(reserves);
        Assert.Equal(reservesDocument.RootElement.GetProperty("total").GetInt32(), total);
    }

    [Fact]
    public async Task RecordedSearchOptionsAreDerivedFromTheRecordingsThatExist()
    {
        using HttpResponseMessage response = await client.GetAsync("/api/recorded/options");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using JsonDocument document = await ReadJsonAsync(response);
        JsonElement channel = Assert.Single(document.RootElement.GetProperty("channels").EnumerateArray().ToArray());
        Assert.Equal(1, channel.GetProperty("cnt").GetInt32());
        Assert.Equal(JsonValueKind.Number, channel.GetProperty("channelId").ValueKind);
        // 種別を持たない録画は genres に出さない。
        Assert.All(
            document.RootElement.GetProperty("genres").EnumerateArray().ToArray(),
            genre => Assert.Equal(JsonValueKind.Number, genre.GetProperty("genre").ValueKind));
    }

    public void Dispose()
    {
        client.Dispose();
        factory.Dispose();
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response) =>
        await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());

    private static void AssertNoCache(HttpResponseMessage response) =>
        Assert.Equal(
            "private, no-cache, no-store, must-revalidate",
            response.Headers.NonValidated["Cache-Control"].ToString());
}
