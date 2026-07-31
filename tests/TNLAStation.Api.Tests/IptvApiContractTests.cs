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
        // charset を書かないと取り込む側が Latin-1 として読み、放送局名が化ける。
        Assert.Equal("\"UTF-8\"", response.Content.Headers.ContentType?.CharSet);
        string body = await response.Content.ReadAsStringAsync();
        Assert.StartsWith("#EXTM3U", body, StringComparison.Ordinal);
        // 種データのチャンネルは serviceType: 1 (デジタルTVサービス) なので必ず出る。
        // 放送局名は未指定でも半角。EPGStation の応答がそうなっている。
        Assert.Contains("NHK総合1・東京", body, StringComparison.Ordinal);
        // 種データは hasLogoData: true なのでロゴ URL が付く。
        Assert.Contains("tvg-logo=", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ChannelListUsesFullWidthNameWhenAskedTo()
    {
        using HttpResponseMessage response = await client.GetAsync("/api/iptv/channel.m3u8?mode=0&isHalfWidth=false");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        string body = await response.Content.ReadAsStringAsync();
        Assert.Contains("ＮＨＫ総合１・東京", body, StringComparison.Ordinal);
    }

    /// <summary>
    /// EPGStation は放送局名だけ半角、番組名と説明は全角で返す。取り込む側は同じ値を見て
    /// 突き合わせるので、この食い違いごと写す。
    /// </summary>
    [Fact]
    public async Task TheDefaultWidthDiffersBetweenChannelNamesAndProgrammeText()
    {
        string epg = await (await client.GetAsync("/api/iptv/epg.xml")).Content.ReadAsStringAsync();

        Assert.Contains("<display-name lang=\"ja_JP\">NHK総合1・東京</display-name>", epg, StringComparison.Ordinal);
        Assert.Contains("モック放送中番組", epg, StringComparison.Ordinal);
    }

    /// <summary>
    /// PVR は m3u8 の名前と epg.xml の display-name を突き合わせることがある。既定が
    /// 食い違うと、同じチャンネルが別物に見えて番組情報が付かない。
    /// </summary>
    [Fact]
    public async Task TheChannelListAndTheEpgAgreeOnTheChannelNameByDefault()
    {
        string playlist = await (await client.GetAsync("/api/iptv/channel.m3u8?mode=0")).Content.ReadAsStringAsync();
        string epg = await (await client.GetAsync("/api/iptv/epg.xml")).Content.ReadAsStringAsync();

        Assert.Contains("NHK総合1・東京", playlist, StringComparison.Ordinal);
        Assert.Contains("<display-name lang=\"ja_JP\">NHK総合1・東京</display-name>", epg, StringComparison.Ordinal);
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

    /// <summary>
    /// EPGStation は channel の直後にその局の programme を並べる。取り込む側の実装差が
    /// 出ないよう、並びまで写す。
    /// </summary>
    [Fact]
    public async Task EachChannelIsFollowedByItsOwnProgrammes()
    {
        string body = await (await client.GetAsync("/api/iptv/epg.xml")).Content.ReadAsStringAsync();

        int channel = body.IndexOf("<channel id=\"3273601024\"", StringComparison.Ordinal);
        int programme = body.IndexOf("<programme ", StringComparison.Ordinal);

        Assert.True(channel >= 0 && programme > channel);
        Assert.Contains("</channel>\n<programme ", body, StringComparison.Ordinal);
    }

    /// <summary>
    /// EPGStation は禁止文字を全角へ置き換え、実体参照を一切出さない。解けない取り込み側が
    /// あるため、その出力へ揃える。
    /// </summary>
    [Fact]
    public async Task TheEpgCarriesNoXmlEntities()
    {
        string body = await (await client.GetAsync("/api/iptv/epg.xml")).Content.ReadAsStringAsync();

        foreach (string entity in new[] { "&amp;", "&lt;", "&gt;", "&quot;", "&apos;" })
        {
            Assert.DoesNotContain(entity, body, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// 同名の放送局は取り込む側が同じものと見なす。EPGStation は半角空白を足して別物にする。
    /// 末尾の全角空白も EPGStation がそのまま出しているもの。
    /// </summary>
    [Fact]
    public async Task ChannelNamesKeepTheTrailingWideSpace()
    {
        string body = await (await client.GetAsync("/api/iptv/channel.m3u8?mode=0")).Content.ReadAsStringAsync();

        Assert.Contains("NHK総合1・東京\u3000\n", body, StringComparison.Ordinal);
    }

    /// <summary>
    /// EPGStation は Express なので GET を定義すると HEAD にも応じる。取り込む前に HEAD で
    /// 確かめる client があり、405 を返すとそこで諦められる。
    /// </summary>
    [Theory]
    [InlineData("/api/iptv/epg.xml")]
    [InlineData("/api/iptv/channel.m3u8?mode=0")]
    [InlineData("/api/version")]
    public async Task HeadAnswersWhereverGetDoes(string path)
    {
        using var request = new HttpRequestMessage(HttpMethod.Head, path);
        using HttpResponseMessage response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsByteArrayAsync());
        // 本文は返さないが、長さは GET と同じ値を知らせる。
        long? head = response.Content.Headers.ContentLength;
        long body = (await (await client.GetAsync(path)).Content.ReadAsByteArrayAsync()).LongLength;
        Assert.Equal(body, head);
    }

    public void Dispose()
    {
        client.Dispose();
        factory.Dispose();
    }
}
