using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TNLAStation.Application.Abstractions;
using TNLAStation.Domain;
using TNLAStation.Infrastructure.Configuration.EpgStation;

namespace TNLAStation.Api.Tests;

/// <summary>
/// <c>GET /api/config</c> を実 config.yml から組み立てたときの JSON を 1 文字単位で固定する。
///
/// 根拠は EPGStation v2.10.0 の <c>src/model/api/config/ConfigApiModel.ts</c>。
/// schema (<c>api.yml</c>) と実装が食い違う箇所 (<c>isEnableTSLiveStream</c>、
/// <c>streamConfig.live.ts</c> 以下、<c>m2ts</c> だけが組) は実装側に合わせている。
/// </summary>
public sealed class ConfigApiFixtureTests : IDisposable
{
    private readonly List<string> temporaryDirectories = [];

    private const string FullConfigYaml = """
        port: 8888
        mirakurunPath: http://localhost:40772
        recorded:
            - name: recorded
              path: /mnt/rec
            - name: tmp
              path: /mnt/tmp
            - name: anime
              path: /mnt/anime
        encode:
            - name: H.264
              cmd: '%NODE% /enc.js'
              suffix: .mp4
            - name: H.265
              cmd: '%NODE% /enc265.js'
              suffix: .mp4
        urlscheme:
            m2ts:
                ios: m2ts-ios
                android: m2ts-android
            video:
                ios: video-ios
            download:
                ios: download-ios
        kodiHosts:
            - name: living
              host: http://192.168.1.10:8080
            - name: bedroom
              host: http://192.168.1.11:8080
        stream:
            live:
                ts:
                    m2ts:
                        - name: 720p
                          cmd: 'ffmpeg-720'
                        - name: 480p
                          cmd: 'ffmpeg-480'
                        - name: 無変換
                    m2tsll:
                        - name: 720p
                          cmd: 'ffmpeg-ll-720'
                    hls:
                        - name: 720p
                          cmd: 'ffmpeg-hls-720'
                        - name: 480p
                          cmd: 'ffmpeg-hls-480'
            recorded:
                ts:
                    mp4:
                        - name: 720p
                          cmd: 'ffmpeg-rec-mp4'
                encoded:
                    hls:
                        - name: 480p
                          cmd: 'ffmpeg-enc-hls'
        """;

    [Fact]
    public async Task TheFullConfigYamlProducesExactlyTheUpstreamJson()
    {
        using WebApplicationFactory<Program> factory = CreateFactory(FullConfigYaml);
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.GetAsync("/api/config");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        string actual = Canonicalise(await response.Content.ReadAsStringAsync());

        // socketIOPort は clientSocketioPort も socketioPort も無いので port と同じ。
        // recorded から tmp が消え、encode/kodiHosts は名前だけ、順序は config.yml のまま。
        // m2ts だけ { name, isUnconverted } の組で、cmd の無い「無変換」だけが true。
        // 書いていない形式 (live webm/mp4, recorded ts webm/hls, encoded webm/mp4) は鍵ごと出ない。
        const string expected = """
            {
              "socketIOPort": 8888,
              "broadcast": {
                "GR": false,
                "BS": false,
                "CS": false,
                "SKY": false
              },
              "recorded": [
                "recorded",
                "anime"
              ],
              "encode": [
                "H.264",
                "H.265"
              ],
              "urlscheme": {
                "m2ts": {
                  "ios": "m2ts-ios",
                  "android": "m2ts-android"
                },
                "video": {
                  "ios": "video-ios"
                },
                "download": {
                  "ios": "download-ios"
                }
              },
              "isEnableTSLiveStream": true,
              "isEnableTSRecordedStream": true,
              "isEnableEncodedRecordedStream": true,
              "streamConfig": {
                "live": {
                  "ts": {
                    "m2ts": [
                      {
                        "name": "720p",
                        "isUnconverted": false
                      },
                      {
                        "name": "480p",
                        "isUnconverted": false
                      },
                      {
                        "name": "無変換",
                        "isUnconverted": true
                      }
                    ],
                    "m2tsll": [
                      "720p"
                    ],
                    "hls": [
                      "720p",
                      "480p"
                    ]
                  }
                },
                "recorded": {
                  "ts": {
                    "mp4": [
                      "720p"
                    ]
                  },
                  "encoded": {
                    "hls": [
                      "480p"
                    ]
                  }
                }
              },
              "kodiHosts": [
                "living",
                "bedroom"
              ]
            }
            """;

        Assert.Equal(Canonicalise(expected), actual);
    }

    [Fact]
    public async Task AStreamBlockWithoutItsTsLayerStillEmitsTheEmptyObjects()
    {
        // EPGStation は stream.live があれば streamConfig.live = {} を必ず作る。
        using WebApplicationFactory<Program> factory = CreateFactory("""
            port: 8888
            urlscheme:
                m2ts: {}
                video: {}
                download: {}
            stream:
                live: {}
                recorded: {}
            """);
        using HttpClient client = factory.CreateClient();

        using JsonDocument document = JsonDocument.Parse(
            await (await client.GetAsync("/api/config")).Content.ReadAsStringAsync());
        JsonElement root = document.RootElement;

        Assert.False(root.GetProperty("isEnableTSLiveStream").GetBoolean());
        Assert.False(root.GetProperty("isEnableTSRecordedStream").GetBoolean());
        Assert.False(root.GetProperty("isEnableEncodedRecordedStream").GetBoolean());

        JsonElement streamConfig = root.GetProperty("streamConfig");
        Assert.Empty(streamConfig.GetProperty("live").EnumerateObject());
        Assert.Empty(streamConfig.GetProperty("recorded").EnumerateObject());
        Assert.Empty(root.GetProperty("urlscheme").GetProperty("m2ts").EnumerateObject());
    }

    [Theory]
    // clientSocketioPort があればアクセス種別に関わらずそれ。
    [InlineData("port: 8888\nsocketioPort: 8889\nclientSocketioPort: 8890\n", false, 8890)]
    [InlineData("port: 8888\nsocketioPort: 8889\nclientSocketioPort: 8890\n", true, 8890)]
    // http は socketioPort → port の順。
    [InlineData("port: 8888\nsocketioPort: 8889\n", false, 8889)]
    [InlineData("port: 8888\n", false, 8888)]
    // https は https.socketioPort → https.port の順。
    [InlineData("port: 8888\nhttps:\n    port: 8443\n    key: k\n    cert: c\n    socketioPort: 8444\n", true, 8444)]
    [InlineData("port: 8888\nhttps:\n    port: 8443\n    key: k\n    cert: c\n", true, 8443)]
    public async Task TheSocketIoPortFollowsUpstreamsResolutionOrder(string yaml, bool isSecure, int expected)
    {
        using WebApplicationFactory<Program> factory = CreateFactory(yaml + """

            urlscheme:
                m2ts: {}
                video: {}
                download: {}
            """);
        using HttpClient client = factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/config");
        if (isSecure)
        {
            request.Headers.Add("X-Forwarded-Proto", "https");
        }

        using HttpResponseMessage response = await client.SendAsync(request);
        using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(expected, document.RootElement.GetProperty("socketIOPort").GetInt32());
    }

    [Fact]
    public async Task AnHttpsRequestWithoutAnHttpsBlockFailsTheWayUpstreamDoes()
    {
        using WebApplicationFactory<Program> factory = CreateFactory("""
            port: 8888
            urlscheme:
                m2ts: {}
                video: {}
                download: {}
            """);
        using HttpClient client = factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/config");
        request.Headers.Add("X-Forwarded-Proto", "https");
        using HttpResponseMessage response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(500, document.RootElement.GetProperty("code").GetInt32());
        Assert.Equal("Internal Server Error", document.RootElement.GetProperty("message").GetString());
        Assert.Equal("httpsConfigError", document.RootElement.GetProperty("errors").GetString());
    }

    /// <summary>
    /// LL-HLS は config.yml ではなく配信サーバーの設定で決まる。上の固定 JSON に
    /// <c>lowlatency</c> が無いことが「未設定なら出さない」側の担保になっている。
    /// </summary>
    [Fact]
    public async Task ConfiguringLowLatencyAddsItsQualitiesToTheLiveStreamChoices()
    {
        using WebApplicationFactory<Program> factory = CreateFactory(FullConfigYaml)
            .WithWebHostBuilder(builder => builder.ConfigureAppConfiguration(configuration =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Streaming:LowLatencyHls:PlaylistUrlTemplate"] = "/lowlatency/live/{streamId}/index.m3u8",
                })));
        using HttpClient client = factory.CreateClient();

        using JsonDocument document = JsonDocument.Parse(
            await (await client.GetAsync("/api/config")).Content.ReadAsStringAsync());

        JsonElement ts = document.RootElement
            .GetProperty("streamConfig").GetProperty("live").GetProperty("ts");
        Assert.Equal(
            ["720p", "480p", "360p"],
            ts.GetProperty("lowlatency").EnumerateArray().Select(mode => mode.GetString()));
        // EPGStation の鍵は触らない。
        Assert.Equal(["720p", "480p"], ts.GetProperty("hls").EnumerateArray().Select(mode => mode.GetString()));
    }

    [Fact]
    public async Task BroadcastComesFromTheTunerTypesMirakurunReports()
    {
        using WebApplicationFactory<Program> factory = CreateFactory(FullConfigYaml)
            .WithWebHostBuilder(builder => builder.ConfigureServices(services =>
                services.AddSingleton<IBroadcastStatusProvider>(
                    new StubBroadcastStatusProvider(new BroadcastAvailability(true, true, false, false)))));
        using HttpClient client = factory.CreateClient();

        using JsonDocument document = JsonDocument.Parse(
            await (await client.GetAsync("/api/config")).Content.ReadAsStringAsync());
        JsonElement broadcast = document.RootElement.GetProperty("broadcast");

        Assert.True(broadcast.GetProperty("GR").GetBoolean());
        Assert.True(broadcast.GetProperty("BS").GetBoolean());
        Assert.False(broadcast.GetProperty("CS").GetBoolean());
        Assert.False(broadcast.GetProperty("SKY").GetBoolean());
    }

    private WebApplicationFactory<Program> CreateFactory(string yaml)
    {
        string root = Path.Combine(Path.GetTempPath(), $"tnla-config-api-{Guid.NewGuid():N}");
        string configDirectory = Path.Combine(root, "config");
        Directory.CreateDirectory(configDirectory);
        File.WriteAllText(Path.Combine(configDirectory, "config.yml"), yaml);
        temporaryDirectories.Add(root);

        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration(configuration => configuration
                .AddEpgStationConfigFile(Path.Combine(configDirectory, "config.yml"), reloadOnChange: false)));
    }

    private static readonly JsonSerializerOptions PrettyJson = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>鍵の順序は EPGStation の代入順で決まるので保つ。空白と改行だけを揃える。</summary>
    private static string Canonicalise(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        return JsonSerializer.Serialize(document.RootElement, PrettyJson);
    }

    private sealed class StubBroadcastStatusProvider(BroadcastAvailability availability) : IBroadcastStatusProvider
    {
        public ValueTask<BroadcastAvailability> GetAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult(availability);
    }

    public void Dispose()
    {
        foreach (string directory in temporaryDirectories)
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (DirectoryNotFoundException)
            {
                // 既に消えているなら何もしない。
            }
        }
    }
}
