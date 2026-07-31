using System.Net.WebSockets;
using System.Text;
using System.Threading.Channels;
using Microsoft.Extensions.Options;
using TNLAStation.Infrastructure.Configuration;
using TNLAStation.Infrastructure.Configuration.EpgStation;

namespace TNLAStation.Api.SocketIo;

/// <summary>
/// EPGStation 互換の socket.io を待ち受ける。
///
/// EPGStation は <c>SocketIOManageModel.initialize</c> で
/// <c>path: subDirectory 無し → '/socket.io'、有り → urljoin(subDirectory, '/socket.io')</c>、
/// <c>cors: { origin: '*' }</c> の Socket.IO サーバーを立てる。ここも同じパスと CORS で待つ。
///
/// 待受ポートの意味は <c>ServiceServer.start</c> のとおり:
/// <c>socketioPort</c> が <c>port</c> と同じ (または未設定) なら HTTP サーバーへ相乗りし、
/// 違えばそのポートで別に待つ。<c>clientSocketioPort</c> は待受先ではなく
/// <c>/api/config</c> が知らせる値なので、ここでは使わない。
/// </summary>
internal static class SocketIoEndpoints
{
    public static IEndpointRouteBuilder MapSocketIoEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // subDirectory は UsePathBase が剥がすので、ここでは常に /socket.io で受ける。
        // EPGStation の urljoin(subDirectory, '/socket.io') と同じ URL になる。
        endpoints.Map("/socket.io/{**rest}", HandleAsync).ExcludeFromDescription();
        endpoints.Map("/socket.io", HandleAsync).ExcludeFromDescription();

        return endpoints;
    }

    /// <summary>
    /// EPGStation が公開しているのは 1 本のパスだけで、転送方式は query の <c>transport</c> で分かれる。
    /// </summary>
    private static async Task HandleAsync(HttpContext context, SocketIoHub hub)
    {
        // EPGStation は cors: { origin: '*' }。socket.io は自前で CORS を付けるので、アプリ全体の
        // isAllowAllCORS とは独立に常に許可する。
        context.Response.Headers.AccessControlAllowOrigin = "*";

        if (HttpMethods.IsOptions(context.Request.Method))
        {
            context.Response.Headers.AccessControlAllowMethods = "GET,HEAD,PUT,PATCH,POST,DELETE";
            context.Response.Headers.AccessControlAllowHeaders = "content-type";
            context.Response.StatusCode = StatusCodes.Status204NoContent;
            return;
        }

        string transport = context.Request.Query["transport"].ToString();
        string sid = context.Request.Query["sid"].ToString();

        if (string.Equals(transport, "websocket", StringComparison.Ordinal) &&
            context.WebSockets.IsWebSocketRequest)
        {
            await HandleWebSocketAsync(context, hub, sid);
            return;
        }

        if (string.Equals(transport, "polling", StringComparison.Ordinal))
        {
            await HandlePollingAsync(context, hub, sid);
            return;
        }

        // engine.io は未知の transport を 400 で返す。
        await WriteEngineErrorAsync(context, StatusCodes.Status400BadRequest, 0, "Transport unknown");
    }


    private static async Task HandlePollingAsync(HttpContext context, SocketIoHub hub, string sid)
    {
        if (string.IsNullOrEmpty(sid))
        {
            // 新規接続。open パケットを 1 つ返して待ち行列を作る。
            string newSid = CreateSid();
            SocketIoSession created = hub.CreateSession(newSid);
            _ = created;
            await WritePollingAsync(context, EngineIoProtocol.Open(newSid, canUpgrade: true));
            return;
        }

        if (!hub.TryGetSession(sid, out SocketIoSession session))
        {
            await WriteEngineErrorAsync(context, StatusCodes.Status400BadRequest, 1, "Session ID unknown");
            return;
        }

        session.LastSeen = DateTimeOffset.UtcNow;

        if (HttpMethods.IsPost(context.Request.Method))
        {
            using var reader = new StreamReader(context.Request.Body, Encoding.UTF8);
            string body = await reader.ReadToEndAsync(context.RequestAborted);
            foreach (string packet in EngineIoProtocol.DecodePayload(body))
            {
                HandleIncoming(session, packet);
            }

            context.Response.ContentType = "text/html; charset=UTF-8";
            await context.Response.WriteAsync("ok", context.RequestAborted);
            return;
        }

        // GET: 送るものが出るまで待つ。engine.io の long-polling。
        List<string> packets = [];
        try
        {
            packets.Add(await session.ReadAsync(context.RequestAborted));
            while (session.TryRead(out string more))
            {
                packets.Add(more);
            }
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (ChannelClosedException)
        {
            return;
        }

        await WritePollingAsync(context, EngineIoProtocol.EncodePayload(packets));
    }

    private static async Task WritePollingAsync(HttpContext context, string payload)
    {
        context.Response.ContentType = "text/plain; charset=UTF-8";
        await context.Response.WriteAsync(payload, context.RequestAborted);
    }


    private static async Task HandleWebSocketAsync(HttpContext context, SocketIoHub hub, string sid)
    {
        bool isUpgrade = !string.IsNullOrEmpty(sid);
        SocketIoSession session;

        if (isUpgrade)
        {
            if (!hub.TryGetSession(sid, out session!))
            {
                await WriteEngineErrorAsync(context, StatusCodes.Status400BadRequest, 1, "Session ID unknown");
                return;
            }
        }
        else
        {
            sid = CreateSid();
            session = hub.CreateSession(sid);
        }

        using WebSocket socket = await context.WebSockets.AcceptWebSocketAsync();

        if (!isUpgrade)
        {
            // 直接 WebSocket で来た場合は upgrade 先が無いので upgrades を空で返す。
            await SendAsync(socket, EngineIoProtocol.Open(sid, canUpgrade: false), context.RequestAborted);
        }

        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
        Task writer = PumpOutboundAsync(socket, session, lifetime.Token);

        try
        {
            await ReadLoopAsync(socket, session, isUpgrade, lifetime.Token);
        }
        finally
        {
            await lifetime.CancelAsync();
            hub.RemoveSession(sid);
            await Task.WhenAny(writer, Task.Delay(TimeSpan.FromSeconds(1), CancellationToken.None));
        }
    }

    private static async Task ReadLoopAsync(
        WebSocket socket,
        SocketIoSession session,
        bool isUpgrade,
        CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[8192];
        var message = new StringBuilder();

        while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
        {
            WebSocketReceiveResult result;
            try
            {
                result = await socket.ReceiveAsync(buffer, cancellationToken);
            }
            catch (Exception exception) when (exception is OperationCanceledException or WebSocketException)
            {
                return;
            }

            if (result.MessageType == WebSocketMessageType.Close)
            {
                return;
            }

            message.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
            if (!result.EndOfMessage)
            {
                continue;
            }

            string packet = message.ToString();
            message.Clear();
            session.LastSeen = DateTimeOffset.UtcNow;

            // upgrade の握手。engine.io は probe を往復させてから 5 で切り替える。
            if (isUpgrade && string.Equals(packet, EngineIoProtocol.ProbePing, StringComparison.Ordinal))
            {
                // WebSocket への送信は writer 1 本だけに限定する。同時 SendAsync は
                // System.Net.WebSockets の契約外で、通知や ping と衝突すると接続が落ちる。
                session.Enqueue(EngineIoProtocol.ProbePong);
                continue;
            }

            if (string.Equals(packet, EngineIoProtocol.Upgrade, StringComparison.Ordinal))
            {
                session.IsUpgraded = true;
                continue;
            }

            HandleIncoming(session, packet);
        }
    }

    private static async Task PumpOutboundAsync(
        WebSocket socket,
        SocketIoSession session,
        CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                string packet = await session.ReadAsync(cancellationToken);
                await SendAsync(socket, packet, cancellationToken);
            }
        }
        catch (Exception exception) when (exception is OperationCanceledException or ChannelClosedException
            or WebSocketException)
        {
            // 接続が閉じた。呼び出し側が後始末する。
        }
    }

    private static Task SendAsync(WebSocket socket, string packet, CancellationToken cancellationToken) =>
        socket.State == WebSocketState.Open
            ? socket.SendAsync(
                new ArraySegment<byte>(Encoding.UTF8.GetBytes(packet)),
                WebSocketMessageType.Text,
                endOfMessage: true,
                cancellationToken)
            : Task.CompletedTask;


    /// <summary>
    /// クライアントから来たパケットを処理する。EPGStation はクライアント発のイベントを 1 つも
    /// 購読していないので、ここも接続・切断・ping/pong だけを扱う。
    /// </summary>
    private static void HandleIncoming(SocketIoSession session, string packet)
    {
        if (packet.Length == 0)
        {
            return;
        }

        switch (packet[0])
        {
            case '2':
                // クライアント発の ping。engine.io v4 では通常来ないが、来たら pong を返す。
                session.Enqueue(EngineIoProtocol.Pong);
                break;

            case '3':
                // pong。生存確認だけなので LastSeen の更新で足りている。
                break;

            case '4' when packet.StartsWith("40", StringComparison.Ordinal):
                session.IsConnected = true;
                session.Enqueue(EngineIoProtocol.Connect(CreateSid()));
                break;

            case '4' when packet.StartsWith("41", StringComparison.Ordinal):
                session.IsConnected = false;
                break;

            default:
                break;
        }
    }

    private static async Task WriteEngineErrorAsync(HttpContext context, int status, int code, string message)
    {
        context.Response.StatusCode = status;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsync(
            $"{{\"code\":{code.ToString(System.Globalization.CultureInfo.InvariantCulture)},\"message\":\"{message}\"}}",
            context.RequestAborted);
    }

    /// <summary>socket.io の id は base64url 20 文字前後。長さと文字種だけ合わせておく。</summary>
    private static string CreateSid() =>
        Convert.ToBase64String(Guid.NewGuid().ToByteArray())
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
}

/// <summary>
/// socket.io を別ポートで待つ構成のための設定解決。<c>socketioPort</c> が <c>port</c> と同じか
/// 未設定なら相乗り、違えばそのポートも Kestrel に開かせる。
/// 根拠: EPGStation/src/model/service/ServiceServer.ts の <c>start()</c>。
/// </summary>
public static class SocketIoListener
{
    /// <summary>socket.io だけを別に待ち受けるポート。相乗りなら null。</summary>
    public static int? ResolveDedicatedPort(ServerOptions server)
    {
        ArgumentNullException.ThrowIfNull(server);

        if (server.SocketIoPort is not { } socketIoPort)
        {
            return null;
        }

        return socketIoPort == server.Port ? null : socketIoPort;
    }

    /// <summary>https 側の socket.io 待受ポート。未設定なら https の待受へ相乗り。</summary>
    public static int? ResolveDedicatedHttpsPort(ServerOptions server)
    {
        ArgumentNullException.ThrowIfNull(server);

        return server.Https?.SocketIoPort;
    }

    /// <summary>socket.io のパス。EPGStation の <c>urljoin(subDirectory, '/socket.io')</c> と同じ。</summary>
    public static string ResolvePath(string? subDirectory) =>
        string.IsNullOrEmpty(subDirectory) ? "/socket.io" : UrlJoin.Join(subDirectory, "/socket.io");
}
