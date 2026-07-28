using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace TNLAStation.Api.Tests;

/// <summary>
/// <c>GET /api/debug</c> は Swagger UI へのリダイレクト。
///
/// 根拠は EPGStation v2.10.0 の <c>src/model/service/ServiceServer.ts</c>:
/// <code>res.redirect(this.createUrl('/api-docs/?url=' + this.createUrl('/api/docs')))</code>
/// subDirectory の有無で <c>url-join</c> を通るかどうかが変わり、Location が 1 文字変わる。
/// </summary>
public sealed class DebugRedirectTests : IDisposable
{
    private readonly List<WebApplicationFactory<Program>> factories = [];

    [Fact]
    public async Task WithoutASubDirectoryTheLocationKeepsTheSlashBeforeTheQuery()
    {
        using HttpClient client = CreateClient(null);
        using HttpResponseMessage response = await client.GetAsync("/api/debug");

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Equal("/api-docs/?url=/api/docs", response.Headers.Location?.OriginalString);
        Assert.Equal("Accept", string.Join(',', response.Headers.Vary));

        // express の res.redirect は本文も書く。Accept が無ければ text/plain。
        Assert.Equal("text/plain", response.Content.Headers.ContentType?.MediaType);
        string body = await response.Content.ReadAsStringAsync();
        Assert.Equal("Found. Redirecting to /api-docs/?url=/api/docs", body);
        Assert.Equal(body.Length, response.Content.Headers.ContentLength);
    }

    [Fact]
    public async Task WithASubDirectoryUrlJoinCollapsesTheSlashBeforeTheQuery()
    {
        using HttpClient client = CreateClient("/tnla");
        using HttpResponseMessage response = await client.GetAsync("/tnla/api/debug");

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        // url-join が '/?' を '?' へ潰すため、subDirectory 有りだけスラッシュが消える。
        Assert.Equal("/tnla/api-docs?url=/tnla/api/docs", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task AnHtmlAcceptGetsTheAnchorBodyExpressWouldSend()
    {
        using HttpClient client = CreateClient(null);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/debug");
        request.Headers.Add("Accept", "text/html");

        using HttpResponseMessage response = await client.SendAsync(request);

        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(
            "<p>Found. Redirecting to <a href=\"/api-docs/?url=/api/docs\">/api-docs/?url=/api/docs</a></p>",
            await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task AWildcardAcceptStillGetsThePlainTextBody()
    {
        using HttpClient client = CreateClient(null);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/debug");
        request.Headers.Add("Accept", "*/*");

        using HttpResponseMessage response = await client.SendAsync(request);

        // express の res.format は同じ品質なら先に登録された text/plain を選ぶ。
        Assert.Equal("text/plain", response.Content.Headers.ContentType?.MediaType);
    }

    private HttpClient CreateClient(string? subDirectory)
    {
        var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration(configuration =>
            {
                if (subDirectory is not null)
                {
                    configuration.AddInMemoryCollection(
                        new Dictionary<string, string?> { ["Api:SubDirectory"] = subDirectory });
                }
            }));
        factories.Add(factory);

        return factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
    }

    public void Dispose()
    {
        foreach (WebApplicationFactory<Program> factory in factories)
        {
            factory.Dispose();
        }
    }
}
