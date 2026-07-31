using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace TNLAStation.Api.Tests;

/// <summary>
/// IPTV 向けの m3u8/xmltv 出力。EPGStation は serviceType でデータ放送等を除外し、
/// ロゴが無いチャンネルには tvg-logo を付けず、番組が 1 つも無いチャンネルは xmltv の
/// channel 要素ごと省く。これらを外すと Kodi 側の表示が EPGStation と変わるので固定する。
/// </summary>
public sealed class IptvApiContractTests : IDisposable
{
    private readonly WebApplicationFactory<Program> factory = new();
    private readonly HttpClient client;

    public IptvApiContractTests()
    {
        client = factory.CreateClient();
    }

    [Fact]
    public async Task ChannelListIsAnM3u8PlaylistWithTheSeededMediaServiceChannel()
    {
        using HttpResponseMessage response = await client.GetAsync("/api/iptv/channel.m3u8?mode=0");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.StartsWith("application/x-mpegURL", response.Content.Headers.ContentType?.MediaType);
        string body = await response.Content.ReadAsStringAsync();
        Assert.StartsWith("#EXTM3U", body, StringComparison.Ordinal);
        // 種データのチャンネルは serviceType: 1 (デジタルTVサービス) なので必ず出る。
        Assert.Contains("NHK総合1・東京", body, StringComparison.Ordinal);
        // 種データは hasLogoData: true なのでロゴ URL が付く。
        Assert.Contains("tvg-logo=", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ChannelListUsesFullWidthNameWhenRequested()
    {
        using HttpResponseMessage response = await client.GetAsync("/api/iptv/channel.m3u8?mode=0&isHalfWidth=false");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        string body = await response.Content.ReadAsStringAsync();
        Assert.Contains("ＮＨＫ総合１・東京", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EpgXmlOnlyIncludesChannelsThatHaveAProgramInTheWindow()
    {
        using HttpResponseMessage response = await client.GetAsync("/api/iptv/epg.xml");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        string body = await response.Content.ReadAsStringAsync();
        Assert.Contains("<tv generator-info-name=\"EPGStation\">", body, StringComparison.Ordinal);
        // 種データの番組は現在放送中なので既定の 3 日分の窓に必ず含まれる。
        Assert.Contains("<channel id=\"3273601024\"", body, StringComparison.Ordinal);
        Assert.Contains("モック放送中番組", body, StringComparison.Ordinal);
    }

    public void Dispose()
    {
        client.Dispose();
        factory.Dispose();
    }
}
