using System.Diagnostics;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TNLAStation.Api.SocketIo;

namespace TNLAStation.Api.Tests;

/// <summary>
/// socket.io の互換試験。
///
/// 上流は <c>EPGStation/src/model/service/socketio/SocketIOManageModel.ts</c> と
/// <c>ServiceServer.ts</c>。公開されているのは <c>updateStatus</c> と <c>updateEncode</c> の
/// 2 イベントだけで、どちらも payload を持たず、200ms で束ねられる。
/// パスは subDirectory が無ければ <c>/socket.io</c>、あれば <c>urljoin(subDirectory, '/socket.io')</c>。
/// </summary>
public sealed class SocketIoCompatibilityTests : IDisposable
{
    private readonly List<WebApplicationFactory<Program>> factories = [];


    [Fact]
    public async Task ThePollingHandshakeAnswersAnEngineIoOpenPacket()
    {
        using HttpClient client = CreateClient(null);

        using HttpResponseMessage response = await client.GetAsync("/socket.io/?EIO=4&transport=polling");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/plain", response.Content.Headers.ContentType?.MediaType);
        // 上流は cors: { origin: '*' } を socket.io サーバー自身に持たせている。
        Assert.Equal("*", string.Join(',', response.Headers.GetValues("Access-Control-Allow-Origin")));

        string payload = await response.Content.ReadAsStringAsync();
        Assert.StartsWith("0{", payload, StringComparison.Ordinal);

        using JsonDocument open = JsonDocument.Parse(payload[1..]);
        Assert.False(string.IsNullOrEmpty(open.RootElement.GetProperty("sid").GetString()));
        Assert.Equal(["websocket"], open.RootElement.GetProperty("upgrades").EnumerateArray().Select(x => x.GetString()));
        Assert.Equal(25_000, open.RootElement.GetProperty("pingInterval").GetInt32());
        Assert.Equal(20_000, open.RootElement.GetProperty("pingTimeout").GetInt32());
    }

    [Fact]
    public async Task AnUnknownSessionIdIsRejectedTheWayEngineIoDoes()
    {
        using HttpClient client = CreateClient(null);

        using HttpResponseMessage response = await client.GetAsync("/socket.io/?EIO=4&transport=polling&sid=nope");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(1, document.RootElement.GetProperty("code").GetInt32());
        Assert.Equal("Session ID unknown", document.RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public async Task ConnectingToTheDefaultNamespaceOverPollingGetsAConnectPacketBack()
    {
        using HttpClient client = CreateClient(null);
        string sid = await HandshakeAsync(client);

        using HttpResponseMessage post = await client.PostAsync(
            $"/socket.io/?EIO=4&transport=polling&sid={sid}",
            new StringContent("40"));
        Assert.Equal(HttpStatusCode.OK, post.StatusCode);
        Assert.Equal("ok", await post.Content.ReadAsStringAsync());

        string packets = await client.GetStringAsync($"/socket.io/?EIO=4&transport=polling&sid={sid}");
        Assert.StartsWith("40{", packets, StringComparison.Ordinal);
        using JsonDocument connect = JsonDocument.Parse(packets[2..]);
        Assert.False(string.IsNullOrEmpty(connect.RootElement.GetProperty("sid").GetString()));
    }

    [Fact]
    public async Task NotifyClientReachesAPollingClientAsUpdateStatus()
    {
        using WebApplicationFactory<Program> factory = CreateFactory(null);
        using HttpClient client = factory.CreateClient();
        string sid = await HandshakeAsync(client);
        await ConnectAsync(client, sid);

        var hub = factory.Services.GetRequiredService<SocketIoHub>();

        // 200ms の間引きを跨いで届くことを確かめる。
        Task<string> poll = client.GetStringAsync($"/socket.io/?EIO=4&transport=polling&sid={sid}");
        hub.NotifyClient();

        Assert.Equal("42[\"updateStatus\"]", await poll.WaitAsync(TimeSpan.FromSeconds(10)));
    }


    [Fact]
    public async Task ADirectWebSocketConnectionCompletesTheEngineIoAndSocketIoHandshakes()
    {
        using WebApplicationFactory<Program> factory = CreateFactory(null);
        using WebSocket socket = await ConnectWebSocketAsync(factory, "/socket.io/?EIO=4&transport=websocket");

        string open = await ReceiveAsync(socket);
        Assert.StartsWith("0{", open, StringComparison.Ordinal);
        using JsonDocument document = JsonDocument.Parse(open[1..]);
        // 直接 WebSocket で来た接続に upgrade 先は無い。
        Assert.Empty(document.RootElement.GetProperty("upgrades").EnumerateArray());

        await SendAsync(socket, "40");
        string connect = await ReceiveAsync(socket);
        Assert.StartsWith("40{", connect, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BothEventsArriveOverWebSocketWithTheirUpstreamNamesAndNoPayload()
    {
        using WebApplicationFactory<Program> factory = CreateFactory(null);
        using WebSocket socket = await ConnectWebSocketAsync(factory, "/socket.io/?EIO=4&transport=websocket");
        await ReceiveAsync(socket);
        await SendAsync(socket, "40");
        await ReceiveAsync(socket);

        var hub = factory.Services.GetRequiredService<SocketIoHub>();
        hub.NotifyClient();
        Assert.Equal("42[\"updateStatus\"]", await ReceiveAsync(socket));

        hub.NotifyUpdateEncodeProgress();
        Assert.Equal("42[\"updateEncode\"]", await ReceiveAsync(socket));
    }

    /// <summary>
    /// 上流は <c>callTimer</c> が立っている間の呼び出しを捨てる。連続で叩いても 1 本しか飛ばない。
    /// </summary>
    [Fact]
    public async Task RepeatedNotificationsInsideTheWindowCollapseIntoOneEvent()
    {
        using WebApplicationFactory<Program> factory = CreateFactory(null);
        using WebSocket socket = await ConnectWebSocketAsync(factory, "/socket.io/?EIO=4&transport=websocket");
        await ReceiveAsync(socket);
        await SendAsync(socket, "40");
        await ReceiveAsync(socket);

        var hub = factory.Services.GetRequiredService<SocketIoHub>();
        for (int i = 0; i < 20; i++)
        {
            hub.NotifyClient();
        }

        Assert.Equal("42[\"updateStatus\"]", await ReceiveAsync(socket));

        // 窓が閉じたあとにもう 1 本来ないこと。
        await Assert.ThrowsAsync<TimeoutException>(() => ReceiveAsync(socket, TimeSpan.FromMilliseconds(600)));
    }

    /// <summary>
    /// <c>updateStatus</c> と <c>updateEncode</c> は別々のタイマーで間引かれるので、
    /// 同じ窓に両方入っても 2 本とも飛ぶ (上流も <c>callTimer</c> と
    /// <c>encodeProgressCallTimer</c> を分けている)。
    /// </summary>
    [Fact]
    public async Task TheTwoEventsAreThrottledIndependently()
    {
        using WebApplicationFactory<Program> factory = CreateFactory(null);
        using WebSocket socket = await ConnectWebSocketAsync(factory, "/socket.io/?EIO=4&transport=websocket");
        await ReceiveAsync(socket);
        await SendAsync(socket, "40");
        await ReceiveAsync(socket);

        var hub = factory.Services.GetRequiredService<SocketIoHub>();
        hub.NotifyClient();
        hub.NotifyUpdateEncodeProgress();

        List<string> received = [await ReceiveAsync(socket), await ReceiveAsync(socket)];
        Assert.Equal(
            ["42[\"updateEncode\"]", "42[\"updateStatus\"]"],
            received.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task UpgradingFromPollingToWebSocketKeepsTheSameSessionAndItsQueue()
    {
        using WebApplicationFactory<Program> factory = CreateFactory(null);
        using HttpClient client = factory.CreateClient();
        string sid = await HandshakeAsync(client);
        await ConnectAsync(client, sid);

        using WebSocket socket = await ConnectWebSocketAsync(
            factory, $"/socket.io/?EIO=4&transport=websocket&sid={sid}");

        // engine.io の upgrade 握手。probe を往復させてから 5 を送る。
        await SendAsync(socket, "2probe");
        Assert.Equal("3probe", await ReceiveAsync(socket));
        await SendAsync(socket, "5");

        var hub = factory.Services.GetRequiredService<SocketIoHub>();
        hub.NotifyClient();

        Assert.Equal("42[\"updateStatus\"]", await ReceiveAsync(socket));
    }

    [Fact]
    public async Task ReconnectingAfterADropStillReceivesEvents()
    {
        using WebApplicationFactory<Program> factory = CreateFactory(null);
        var hub = factory.Services.GetRequiredService<SocketIoHub>();

        using (WebSocket first = await ConnectWebSocketAsync(factory, "/socket.io/?EIO=4&transport=websocket"))
        {
            await ReceiveAsync(first);
            await SendAsync(first, "40");
            await ReceiveAsync(first);

            // 通信が切れた状態を作る。閉じる手続きを踏まずに落ちても、次の接続は普通に張れる。
            first.Abort();
        }

        using WebSocket second = await ConnectWebSocketAsync(factory, "/socket.io/?EIO=4&transport=websocket");
        await ReceiveAsync(second);
        await SendAsync(second, "40");
        await ReceiveAsync(second);

        hub.NotifyClient();
        Assert.Equal("42[\"updateStatus\"]", await ReceiveAsync(second));
    }

    /// <summary>
    /// 状態を変える要求のあとに <c>updateStatus</c> が飛ぶこと。上流は EventSetter が繋いだ
    /// イベント経由で <c>ipc.notifyClient()</c> を呼ぶ。
    /// </summary>
    [Fact]
    public async Task AMutatingRequestPushesUpdateStatusToConnectedClients()
    {
        using WebApplicationFactory<Program> factory = CreateFactory(null);
        using WebSocket socket = await ConnectWebSocketAsync(factory, "/socket.io/?EIO=4&transport=websocket");
        await ReceiveAsync(socket);
        await SendAsync(socket, "40");
        await ReceiveAsync(socket);

        using HttpClient client = factory.CreateClient();
        using HttpResponseMessage response = await client.PostAsync("/api/reserves/update", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Assert.Equal("42[\"updateStatus\"]", await ReceiveAsync(socket));
    }

    /// <summary>参照系では鳴らない。上流も読み取りではイベントを発火しない。</summary>
    [Fact]
    public async Task AReadOnlyRequestDoesNotPushAnything()
    {
        using WebApplicationFactory<Program> factory = CreateFactory(null);
        using WebSocket socket = await ConnectWebSocketAsync(factory, "/socket.io/?EIO=4&transport=websocket");
        await ReceiveAsync(socket);
        await SendAsync(socket, "40");
        await ReceiveAsync(socket);

        using HttpClient client = factory.CreateClient();
        using HttpResponseMessage response = await client.GetAsync("/api/reserves?isHalfWidth=false");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await Assert.ThrowsAsync<TimeoutException>(() => ReceiveAsync(socket, TimeSpan.FromMilliseconds(600)));
    }


    [Fact]
    public async Task WithASubDirectoryTheServerListensUnderThatPrefix()
    {
        using HttpClient client = CreateClient("/tnla");

        using HttpResponseMessage inside = await client.GetAsync("/tnla/socket.io/?EIO=4&transport=polling");
        Assert.Equal(HttpStatusCode.OK, inside.StatusCode);
        Assert.StartsWith("0{", await inside.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        // subDirectory を付けた構成では、素の /socket.io は使えない。
        using HttpResponseMessage outside = await client.GetAsync("/socket.io/?EIO=4&transport=polling");
        Assert.Equal(HttpStatusCode.NotFound, outside.StatusCode);
    }

    [Fact]
    public void TheSocketIoPathFollowsUpstreamsUrlJoin()
    {
        Assert.Equal("/socket.io", SocketIoListener.ResolvePath(null));
        Assert.Equal("/socket.io", SocketIoListener.ResolvePath(string.Empty));
        Assert.Equal("/tnla/socket.io", SocketIoListener.ResolvePath("/tnla"));
    }

    /// <summary>
    /// 上流の <c>ServiceServer.start</c> は <c>socketioPort === port</c> のとき HTTP サーバーへ
    /// 相乗りし、違うときだけ別の待受を作る。<c>clientSocketioPort</c> は待受先に影響しない。
    /// </summary>
    [Theory]
    [InlineData(8888, null, null)]
    [InlineData(8888, 8888, null)]
    [InlineData(8888, 8889, 8889)]
    public void ADedicatedSocketIoPortIsOnlyOpenedWhenItDiffersFromTheHttpPort(
        int port,
        int? socketIoPort,
        int? expected)
    {
        var server = new TNLAStation.Infrastructure.Configuration.ServerOptions
        {
            Port = port,
            SocketIoPort = socketIoPort,
            ClientSocketIoPort = 9999,
        };

        Assert.Equal(expected, SocketIoListener.ResolveDedicatedPort(server));
    }


    private WebApplicationFactory<Program> CreateFactory(string? subDirectory)
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
        return factory;
    }

    private HttpClient CreateClient(string? subDirectory) => CreateFactory(subDirectory).CreateClient();

    private static async Task<string> HandshakeAsync(HttpClient client)
    {
        string payload = await client.GetStringAsync("/socket.io/?EIO=4&transport=polling");
        using JsonDocument document = JsonDocument.Parse(payload[1..]);
        return document.RootElement.GetProperty("sid").GetString()!;
    }

    private static async Task ConnectAsync(HttpClient client, string sid)
    {
        using HttpResponseMessage post = await client.PostAsync(
            $"/socket.io/?EIO=4&transport=polling&sid={sid}",
            new StringContent("40"));
        post.EnsureSuccessStatusCode();

        // CONNECT の応答を取り出しておく。残しておくと次の long-poll が先にそれを返してしまう。
        await client.GetStringAsync($"/socket.io/?EIO=4&transport=polling&sid={sid}");
    }

    private static async Task<WebSocket> ConnectWebSocketAsync(
        WebApplicationFactory<Program> factory,
        string relativeUri)
    {
        WebSocketClient client = factory.Server.CreateWebSocketClient();
        return await client.ConnectAsync(
            new Uri(factory.Server.BaseAddress, relativeUri),
            CancellationToken.None);
    }

    private static Task SendAsync(WebSocket socket, string packet) =>
        socket.SendAsync(
            new ArraySegment<byte>(Encoding.UTF8.GetBytes(packet)),
            WebSocketMessageType.Text,
            endOfMessage: true,
            CancellationToken.None);

    private static async Task<string> ReceiveAsync(WebSocket socket, TimeSpan? timeout = null)
    {
        using var cancellation = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(10));
        byte[] buffer = new byte[8192];
        var builder = new StringBuilder();

        try
        {
            while (true)
            {
                WebSocketReceiveResult result = await socket.ReceiveAsync(buffer, cancellation.Token);
                builder.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                if (result.EndOfMessage)
                {
                    return builder.ToString();
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw new TimeoutException($"No socket.io packet arrived within {timeout ?? TimeSpan.FromSeconds(10)}.");
        }
    }

    public void Dispose()
    {
        foreach (WebApplicationFactory<Program> factory in factories)
        {
            factory.Dispose();
        }
    }
}
