using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace TNLAStation.Api.Tests;

/// <summary>
/// Fixes the EPG surface (channels, schedules, storages) against EPGStation v2.10.0. The API runs on
/// the in-memory EPG store here, so the assertions cover the HTTP contract rather than sync behavior.
/// </summary>
public sealed class EpgApiContractTests : IDisposable
{
    private const long SeededChannelId = 3_273_601_024;
    private const long SeededProgramId = 327_360_102_400_123;

    private readonly WebApplicationFactory<Program> factory = new();
    private readonly HttpClient client;

    public EpgApiContractTests()
    {
        client = factory.CreateClient();
    }

    [Fact]
    public async Task ChannelsUseUpstreamKeysAndNonCacheHeaders()
    {
        using HttpResponseMessage response = await client.GetAsync("/api/channels");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        AssertNoCache(response);
        using JsonDocument document = await ReadJsonAsync(response);
        JsonElement channel = Assert.Single(document.RootElement.EnumerateArray().ToArray());

        Assert.Equal(SeededChannelId, channel.GetProperty("id").GetInt64());
        Assert.Equal(1024, channel.GetProperty("serviceId").GetInt32());
        Assert.Equal(32736, channel.GetProperty("networkId").GetInt32());
        Assert.Equal("ＮＨＫ総合１・東京", channel.GetProperty("name").GetString());
        Assert.Equal("NHK総合1・東京", channel.GetProperty("halfWidthName").GetString());
        Assert.True(channel.GetProperty("hasLogoData").GetBoolean());
        Assert.Equal("GR", channel.GetProperty("channelType").GetString());
        Assert.Equal("27", channel.GetProperty("channel").GetString());
        Assert.Equal(1, channel.GetProperty("remoteControlKeyId").GetInt32());
        Assert.Equal(1, channel.GetProperty("type").GetInt32());
    }

    [Fact]
    public async Task SchedulesReturnChannelItemWithoutChannelNumberAndHonorIsHalfWidth()
    {
        long startAt = DateTimeOffset.UtcNow.AddHours(-2).ToUnixTimeMilliseconds();
        long endAt = DateTimeOffset.UtcNow.AddHours(2).ToUnixTimeMilliseconds();

        using HttpResponseMessage response = await client.GetAsync(
            $"/api/schedules?startAt={startAt}&endAt={endAt}&isHalfWidth=true&GR=true&BS=false&CS=false&SKY=false");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        AssertNoCache(response);
        using JsonDocument document = await ReadJsonAsync(response);
        JsonElement schedule = Assert.Single(document.RootElement.EnumerateArray().ToArray());

        JsonElement channel = schedule.GetProperty("channel");
        Assert.Equal("NHK総合1・東京", channel.GetProperty("name").GetString());
        // ScheduleChannleItem has neither halfWidthName nor the physical channel number.
        Assert.False(channel.TryGetProperty("halfWidthName", out _));
        Assert.False(channel.TryGetProperty("channel", out _));

        JsonElement program = Assert.Single(schedule.GetProperty("programs").EnumerateArray().ToArray());
        Assert.Equal(SeededProgramId, program.GetProperty("id").GetInt64());
        Assert.Equal(SeededChannelId, program.GetProperty("channelId").GetInt64());
        Assert.True(program.GetProperty("isFree").GetBoolean());
        Assert.Equal(JsonValueKind.Number, program.GetProperty("startAt").ValueKind);
        Assert.Equal(0, program.GetProperty("genre1").GetInt32());
        // rawExtended is only emitted when the caller asks for it.
        Assert.False(program.TryGetProperty("rawExtended", out _));
        Assert.False(program.TryGetProperty("genre2", out _));
        Assert.False(program.TryGetProperty("videoStreamContent", out _));
    }

    [Fact]
    public async Task SchedulesEmitRawExtendedOnlyWhenRequested()
    {
        long startAt = DateTimeOffset.UtcNow.AddHours(-2).ToUnixTimeMilliseconds();
        long endAt = DateTimeOffset.UtcNow.AddHours(2).ToUnixTimeMilliseconds();

        using HttpResponseMessage response = await client.GetAsync(
            $"/api/schedules?startAt={startAt}&endAt={endAt}&isHalfWidth=false&needsRawExtended=true" +
            "&GR=true&BS=false&CS=false&SKY=false");

        using JsonDocument document = await ReadJsonAsync(response);
        JsonElement program = document.RootElement[0].GetProperty("programs")[0];
        Assert.Equal("固定データ", program.GetProperty("rawExtended").GetProperty("補足").GetString());
    }

    [Fact]
    public async Task BroadcastingSchedulesReturnAtMostOneProgramPerChannel()
    {
        using HttpResponseMessage response = await client.GetAsync("/api/schedules/broadcasting?isHalfWidth=false");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using JsonDocument document = await ReadJsonAsync(response);
        JsonElement schedule = Assert.Single(document.RootElement.EnumerateArray().ToArray());
        Assert.Single(schedule.GetProperty("programs").EnumerateArray().ToArray());
    }

    [Fact]
    public async Task ScheduleDetailReturnsUpstream404Shape()
    {
        using HttpResponseMessage response = await client.GetAsync("/api/schedules/detail/1?isHalfWidth=false");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.False(response.Headers.Contains("Cache-Control"));
        using JsonDocument document = await ReadJsonAsync(response);
        Assert.Equal(404, document.RootElement.GetProperty("code").GetInt32());
        Assert.Equal("program is not found", document.RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public async Task ScheduleSearchTakesTheOptionEnvelopeAndLimit()
    {
        var request = new
        {
            option = new { keyword = "モック", name = true, GR = true },
            isHalfWidth = false,
            limit = 5
        };

        using HttpResponseMessage response = await client.PostAsJsonAsync("/api/schedules/search", request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        AssertNoCache(response);
        using JsonDocument document = await ReadJsonAsync(response);
        JsonElement program = Assert.Single(document.RootElement.EnumerateArray().ToArray());
        Assert.Equal(SeededProgramId, program.GetProperty("id").GetInt64());
    }

    [Fact]
    public async Task ChannelLogoReturns404ForUnknownChannel()
    {
        using HttpResponseMessage response = await client.GetAsync("/api/channels/1/logo");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        using JsonDocument document = await ReadJsonAsync(response);
        Assert.Equal("log file is not found", document.RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public async Task ChannelLogoReturnsPngForChannelWithLogoData()
    {
        using HttpResponseMessage response = await client.GetAsync($"/api/channels/{SeededChannelId}/logo");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("image/png", response.Content.Headers.ContentType?.MediaType);
        Assert.False(response.Headers.Contains("Cache-Control"));
    }

    [Fact]
    public async Task StoragesReportEveryConfiguredRecordedDirectory()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"tnla-storage-api-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        await File.WriteAllBytesAsync(Path.Combine(directory, "sample.ts"), new byte[12]);
        await File.WriteAllBytesAsync(Path.Combine(directory, "sample.drop.log"), new byte[4]);

        try
        {
            using WebApplicationFactory<Program> configuredFactory = factory.WithWebHostBuilder(builder =>
                builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        ["Storage:RecordedDirectories:0:Name"] = "recorded",
                        ["Storage:RecordedDirectories:0:Path"] = directory
                    })));
            using HttpClient configuredClient = configuredFactory.CreateClient();

            using HttpResponseMessage response = await configuredClient.GetAsync("/api/storages");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            AssertNoCache(response);
            using JsonDocument document = await ReadJsonAsync(response);
            JsonElement item = Assert.Single(document.RootElement.GetProperty("items").EnumerateArray().ToArray());
            Assert.Equal("recorded", item.GetProperty("name").GetString());
            var drive = new DriveInfo(directory);
            Assert.Equal(drive.TotalSize, item.GetProperty("total").GetInt64());
            Assert.InRange(item.GetProperty("used").GetInt64(), 0, drive.TotalSize);
            Assert.InRange(item.GetProperty("available").GetInt64(), 0, drive.TotalSize);

            Assert.False(item.TryGetProperty("fileTypes", out _));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task StoragesReturnAnEmptyListWhenNoDirectoryIsConfigured()
    {
        using HttpResponseMessage response = await client.GetAsync("/api/storages");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using JsonDocument document = await ReadJsonAsync(response);
        Assert.Empty(document.RootElement.GetProperty("items").EnumerateArray().ToArray());
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
