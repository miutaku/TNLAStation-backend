using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace TNLAStation.Api.Tests;

/// <summary>
/// Postgres/Mirakurun/ffmpeg-worker が無いテスト用ホストでは、ストリーム・サムネイル関連の
/// 依存が「常に見つからない」スタブ (<c>UnavailableLiveStreamService</c>/<c>UnavailableThumbnailService</c>)
/// になる。これを逆手に取って、上流が明示的に存在チェックをしている操作 (stream の keep、
/// thumbnail の削除) が、対象が無いときに 500 になることを確かめる。
/// </summary>
public sealed class NotFoundMutationTests : IDisposable
{
    private readonly WebApplicationFactory<Program> factory = new();
    private readonly HttpClient client;

    public NotFoundMutationTests()
    {
        client = factory.CreateClient();
    }

    [Fact]
    public async Task KeepingAStreamThatDoesNotExistFails()
    {
        // 上流 (StreamManageModel.keep) は該当ストリームが無いと StreamIsUndefined を投げる。
        using HttpResponseMessage response = await client.PutAsync("/api/streams/999999/keep", content: null);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
    }

    [Fact]
    public async Task StoppingAStreamThatDoesNotExistStillAnswers200()
    {
        // stop はここを無視して常に 200 を返す (upstream StreamManageModel.stop と同じ)。
        using HttpResponseMessage response = await client.DeleteAsync("/api/streams/999999");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task DeletingAThumbnailThatDoesNotExistFails()
    {
        // 上流 (ThumbnailManageModel.delete) は該当サムネイルが無いと ThumbnailIsNotFound を投げる。
        using HttpResponseMessage response = await client.DeleteAsync("/api/thumbnails/999999");

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
    }

    [Fact]
    public async Task GettingDurationForAMissingVideoFileFailsWith500NotFound()
    {
        // 上流 (VideoApiModel.getDuration) は 404 ではなく VideoFileIsUndefined 例外にしており、
        // 汎用の 500 として返る。同じ動画に対する GET (ファイル取得) の 404 とは扱いが違う。
        using HttpResponseMessage response = await client.GetAsync("/api/videos/999999/duration");

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
    }

    [Fact]
    public async Task SendingToAnUnconfiguredKodiHostFails()
    {
        // 上流 (VideoApiModel.sendToKodi) は送り先名が config に無いと KodiHostIsUndefined を
        // 投げる。テスト用ホストには kodi 送り先が設定されていない。
        using HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/videos/1/kodi",
            new { kodiName = "存在しないKodi" });

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
    }

    public void Dispose()
    {
        client.Dispose();
        factory.Dispose();
    }
}
