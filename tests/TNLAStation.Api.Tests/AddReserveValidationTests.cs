using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace TNLAStation.Api.Tests;

/// <summary>
/// 上流の予約追加は、番組指定なら「既に何かがその番組を掴んでいないか」、時刻指定なら
/// 「終了時刻が過去でないか」「同じ条件の予約が既にないか」をチェックし、失敗はどれも
/// 汎用のサーバエラー (500 + code/message/errors) として返る。専用の 400 は無い。
/// </summary>
public sealed class AddReserveValidationTests : IDisposable
{
    private readonly WebApplicationFactory<Program> factory = new();
    private readonly HttpClient client;

    public AddReserveValidationTests()
    {
        client = factory.CreateClient();
    }

    [Fact]
    public async Task AddingAProgramReserveThatIsAlreadyReservedFails()
    {
        var request = new { allowEndLack = false, programId = 999_001L };
        using HttpResponseMessage first = await client.PostAsJsonAsync("/api/reserves", request);
        first.EnsureSuccessStatusCode();

        using HttpResponseMessage response = await client.PostAsJsonAsync("/api/reserves", request);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        using JsonDocument document = await ReadJsonAsync(response);
        Assert.Equal(500, document.RootElement.GetProperty("code").GetInt32());
        Assert.Contains("ReservationManageModelReservedError", document.RootElement.GetProperty("errors").GetString());
    }

    [Fact]
    public async Task AddingATimeSpecifiedReserveThatAlreadyEndedFails()
    {
        var request = new
        {
            allowEndLack = false,
            timeSpecifiedOption = new
            {
                name = "既に終わった予約",
                channelId = 4L,
                startAt = 1_000_000_000_000L,
                endAt = 1_000_000_001_000L,
            },
        };

        using HttpResponseMessage response = await client.PostAsJsonAsync("/api/reserves", request);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        using JsonDocument document = await ReadJsonAsync(response);
        Assert.Contains("TimeSpecifiedOptionError", document.RootElement.GetProperty("errors").GetString());
    }

    [Fact]
    public async Task AddingTheSameTimeSpecifiedReserveTwiceFails()
    {
        var request = new
        {
            allowEndLack = false,
            timeSpecifiedOption = new
            {
                name = "二重に入れる予約",
                channelId = 5L,
                startAt = 2_000_000_000_000L,
                endAt = 2_000_001_800_000L,
            },
        };
        using HttpResponseMessage first = await client.PostAsJsonAsync("/api/reserves", request);
        first.EnsureSuccessStatusCode();

        using HttpResponseMessage response = await client.PostAsJsonAsync("/api/reserves", request);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        using JsonDocument document = await ReadJsonAsync(response);
        Assert.Contains("AddReservationConflictError", document.RootElement.GetProperty("errors").GetString());
    }

    [Fact]
    public async Task AddingAReserveWithAnUnconfiguredEncodeModeFails()
    {
        // 上流 (checkManualReserveOption -> checkEncodeOption) は config に無いモード名を
        // 拒む。テスト用ホストには encode モードが設定されていない。
        var request = new
        {
            allowEndLack = false,
            programId = 999_002L,
            encodeOption = new { mode1 = "H.264", isDeleteOriginalAfterEncode = false },
        };

        using HttpResponseMessage response = await client.PostAsJsonAsync("/api/reserves", request);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        using JsonDocument document = await ReadJsonAsync(response);
        Assert.Contains("AddReservationOptionError", document.RootElement.GetProperty("errors").GetString());
    }

    [Fact]
    public async Task EditingAReserveWithAnUnconfiguredEncodeModeFails()
    {
        var addRequest = new
        {
            allowEndLack = false,
            timeSpecifiedOption = new
            {
                name = "編集される予約",
                channelId = 6L,
                startAt = 2_000_000_000_000L,
                endAt = 2_000_001_800_000L,
            },
        };
        using HttpResponseMessage added = await client.PostAsJsonAsync("/api/reserves", addRequest);
        using JsonDocument addedDocument = await ReadJsonAsync(added);
        long reserveId = addedDocument.RootElement.GetProperty("reserveId").GetInt64();

        var editRequest = new
        {
            allowEndLack = true,
            encodeOption = new { mode1 = "H.264", isDeleteOriginalAfterEncode = false },
        };
        using HttpResponseMessage response = await client.PutAsJsonAsync($"/api/reserves/{reserveId}", editRequest);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        using JsonDocument document = await ReadJsonAsync(response);
        Assert.Contains("ReservationEditError", document.RootElement.GetProperty("errors").GetString());
    }

    public void Dispose()
    {
        client.Dispose();
        factory.Dispose();
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response) =>
        await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
}
