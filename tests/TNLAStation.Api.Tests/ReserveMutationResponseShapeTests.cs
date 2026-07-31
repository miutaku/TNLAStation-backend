using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace TNLAStation.Api.Tests;

/// <summary>
/// EPGStation は予約の単純な変更系 API に 204 ではなく、本文にも状態コードを載せた 200 (更新だけは
/// 201) を返す。ただし予約 1 件を対象にする delete/skip 解除/overlap 解除/更新は、存在チェックを
/// して無ければ例外 (汎用 500 + errors にエラー名) になる — ここは他の一括系エンドポイントとは
/// 違う。実際の応答を固定する。
/// </summary>
public sealed class ReserveMutationResponseShapeTests : IDisposable
{
    private readonly WebApplicationFactory<Program> factory = new();
    private readonly HttpClient client;

    public ReserveMutationResponseShapeTests()
    {
        client = factory.CreateClient();
    }

    [Fact]
    public async Task DeletingAReserveAnswersWithEmptyBodyStatus200ButFailsWhenAlreadyGone()
    {
        long reserveId = await AddManualReserveAsync("消す予約");

        using HttpResponseMessage response = await client.DeleteAsync($"/api/reserves/{reserveId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // EPGStation (cancel) は消えた後にもう一度消そうとすると ReservationIsNotFound で 500 になる。
        using HttpResponseMessage again = await client.DeleteAsync($"/api/reserves/{reserveId}");
        Assert.Equal(HttpStatusCode.InternalServerError, again.StatusCode);

        using HttpResponseMessage missing = await client.DeleteAsync("/api/reserves/999999");
        Assert.Equal(HttpStatusCode.InternalServerError, missing.StatusCode);
        using JsonDocument document = await ReadJsonAsync(missing);
        Assert.Contains("ReservationIsNotFound", document.RootElement.GetProperty("errors").GetString());
    }

    [Fact]
    public async Task CancellingSkipAndOverlapFailWhenTheReserveDoesNotExist()
    {
        using HttpResponseMessage missingSkip = await client.DeleteAsync("/api/reserves/999999/skip");
        Assert.Equal(HttpStatusCode.InternalServerError, missingSkip.StatusCode);

        using HttpResponseMessage missingOverlap = await client.DeleteAsync("/api/reserves/999999/overlap");
        Assert.Equal(HttpStatusCode.InternalServerError, missingOverlap.StatusCode);
    }

    [Fact]
    public async Task CancellingSkipAndOverlapOnAManualReserveNoOpButStillAnswer200()
    {
        // 手動予約はそもそもルール予約ではないので、EPGStation はここで何もせず 200 を返す。
        long reserveId = await AddManualReserveAsync("手動予約はskip対象外");

        using HttpResponseMessage skip = await client.DeleteAsync($"/api/reserves/{reserveId}/skip");
        Assert.Equal(HttpStatusCode.OK, skip.StatusCode);

        using HttpResponseMessage overlap = await client.DeleteAsync($"/api/reserves/{reserveId}/overlap");
        Assert.Equal(HttpStatusCode.OK, overlap.StatusCode);
    }

    [Fact]
    public async Task UpdatingAReserveAnswersWith201AndACodeMessageBody()
    {
        long reserveId = await AddManualReserveAsync("更新する予約");

        using HttpResponseMessage response = await client.PutAsJsonAsync(
            $"/api/reserves/{reserveId}",
            new { allowEndLack = true });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using JsonDocument document = await ReadJsonAsync(response);
        Assert.Equal(201, document.RootElement.GetProperty("code").GetInt32());
        Assert.Equal("ok", document.RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public async Task UpdatingAReserveThatDoesNotExistFails()
    {
        using HttpResponseMessage response = await client.PutAsJsonAsync(
            "/api/reserves/999999",
            new { allowEndLack = true });

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        using JsonDocument document = await ReadJsonAsync(response);
        Assert.Contains("ReservationIsNotFound", document.RootElement.GetProperty("errors").GetString());
    }

    [Fact]
    public async Task UpdateReservesAnswersWithACodeBody()
    {
        using HttpResponseMessage response = await client.PostAsync("/api/reserves/update", content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using JsonDocument document = await ReadJsonAsync(response);
        Assert.Equal(200, document.RootElement.GetProperty("code").GetInt32());
    }

    public void Dispose()
    {
        client.Dispose();
        factory.Dispose();
    }

    private async Task<long> AddManualReserveAsync(string name)
    {
        var request = new
        {
            allowEndLack = false,
            timeSpecifiedOption = new
            {
                name,
                channelId = 2L,
                startAt = 2_000_000_000_000L,
                endAt = 2_000_001_800_000L,
            },
        };

        using HttpResponseMessage response = await client.PostAsJsonAsync("/api/reserves", request);
        using JsonDocument document = await ReadJsonAsync(response);
        return document.RootElement.GetProperty("reserveId").GetInt64();
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response) =>
        await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
}
