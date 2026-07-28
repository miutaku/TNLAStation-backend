using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace TNLAStation.Api.Tests;

/// <summary>
/// 上流 (videos/upload の post) はファイルが無いと FileIsNotFound、紐づけ先の録画が無い
/// (recordedId が数値ですらない場合を含む) と RecordedIdIsNull を投げ、どちらも汎用の 500 に
/// なる。専用の 400/404 は無い。既定のテスト用ホストは Postgres 抜きで
/// <c>EmptyVideoFileRepository</c> (常に null を返す) を使うので、DI を差し替えずに検証できる。
/// </summary>
public sealed class VideoUploadValidationTests : IDisposable
{
    private readonly WebApplicationFactory<Program> factory = new();
    private readonly HttpClient client;

    public VideoUploadValidationTests()
    {
        client = factory.CreateClient();
    }

    [Fact]
    public async Task UploadingWithoutAFileFails()
    {
        using var content = new MultipartFormDataContent
        {
            { new StringContent("1"), "recordedId" },
        };

        using HttpResponseMessage response = await client.PostAsync("/api/videos/upload", content);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        using JsonDocument document = await ReadJsonAsync(response);
        Assert.Contains("FileIsNotFound", document.RootElement.GetProperty("errors").GetString());
    }

    [Fact]
    public async Task UploadingForARecordedItemThatDoesNotExistFails()
    {
        using var fileContent = new ByteArrayContent([1, 2, 3]);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("video/mp4");
        using var content = new MultipartFormDataContent
        {
            { new StringContent("999999"), "recordedId" },
            { fileContent, "file", "video.mp4" },
        };

        using HttpResponseMessage response = await client.PostAsync("/api/videos/upload", content);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        using JsonDocument document = await ReadJsonAsync(response);
        Assert.Contains("RecordedIdIsNull", document.RootElement.GetProperty("errors").GetString());
    }

    [Fact]
    public async Task UploadingWithANonNumericRecordedIdFails()
    {
        using var fileContent = new ByteArrayContent([1, 2, 3]);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("video/mp4");
        using var content = new MultipartFormDataContent
        {
            { new StringContent("not-a-number"), "recordedId" },
            { fileContent, "file", "video.mp4" },
        };

        using HttpResponseMessage response = await client.PostAsync("/api/videos/upload", content);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        using JsonDocument document = await ReadJsonAsync(response);
        Assert.Contains("RecordedIdIsNull", document.RootElement.GetProperty("errors").GetString());
    }

    public void Dispose()
    {
        client.Dispose();
        factory.Dispose();
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response) =>
        await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
}
