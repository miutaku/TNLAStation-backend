using System.Collections.Concurrent;
using System.Threading.Channels;

namespace TNLAStation.Api.SocketIo;

/// <summary>
/// クライアントへ状態変更を知らせる。
///
/// 上流は <c>EPGStation/src/model/service/socketio/SocketIOManageModel.ts</c>。公開されている
/// イベントは 2 つだけで、どちらも payload を持たない:
/// <c>updateStatus</c> (<c>notifyClient</c>) と <c>updateEncode</c>
/// (<c>notifyUpdateEncodeProgress</c>)。どちらも 200ms の遅延で束ねられ、その間に何度呼ばれても
/// 1 回しか飛ばない。連続する更新でクライアントを叩き続けないための間引きで、これも外から見える。
/// </summary>
public sealed class SocketIoHub : IDisposable
{
    /// <summary>上流の <c>setTimeout(..., 200)</c> と同じ間引き幅。</summary>
    internal static readonly TimeSpan NotifyDelay = TimeSpan.FromMilliseconds(200);

    private readonly ConcurrentDictionary<string, SocketIoSession> sessions = new(StringComparer.Ordinal);
    private readonly Lock gate = new();
    private readonly Timer reaper;
    private Timer? statusTimer;
    private Timer? encodeTimer;
    private bool disposed;

    public SocketIoHub()
    {
        reaper = new Timer(
            _ => ReapExpiredSessions(),
            null,
            TimeSpan.FromSeconds(5),
            TimeSpan.FromSeconds(5));
    }

    public IReadOnlyCollection<SocketIoSession> Sessions => (IReadOnlyCollection<SocketIoSession>)sessions.Values;

    public SocketIoSession CreateSession(string sid)
    {
        var session = new SocketIoSession(sid);
        sessions[sid] = session;
        return session;
    }

    public bool TryGetSession(string sid, out SocketIoSession session) => sessions.TryGetValue(sid, out session!);

    public void RemoveSession(string sid)
    {
        if (sessions.TryRemove(sid, out SocketIoSession? session))
        {
            session.Complete();
        }
    }

    /// <summary>上流の <c>notifyClient</c>。200ms 後に <c>updateStatus</c> を 1 回だけ流す。</summary>
    public void NotifyClient() => Schedule(ref statusTimer, "updateStatus");

    /// <summary>
    /// 上流の <c>notifyUpdateEncodeProgress</c>。<c>updateStatus</c> とは別枠で間引かれるので、
    /// 同じ 200ms の窓に両方が入っても 2 本とも飛ぶ。
    /// </summary>
    public void NotifyUpdateEncodeProgress() => Schedule(ref encodeTimer, "updateEncode");

    private void Schedule(ref Timer? slot, string eventName)
    {
        lock (gate)
        {
            if (disposed || slot is not null)
            {
                return;
            }

            slot = new Timer(_ => Fire(eventName), null, NotifyDelay, Timeout.InfiniteTimeSpan);
        }
    }

    private void Fire(string eventName)
    {
        lock (gate)
        {
            if (string.Equals(eventName, "updateStatus", StringComparison.Ordinal))
            {
                statusTimer?.Dispose();
                statusTimer = null;
            }
            else
            {
                encodeTimer?.Dispose();
                encodeTimer = null;
            }
        }

        Broadcast(EngineIoProtocol.Event(eventName));
    }

    /// <summary>間引きを通さずに今すぐ流す。試験と、間引きの意味がない経路のため。</summary>
    internal void Broadcast(string packet)
    {
        foreach (SocketIoSession session in sessions.Values)
        {
            session.Enqueue(packet);
        }
    }

    private void ReapExpiredSessions()
    {
        DateTimeOffset cutoff = DateTimeOffset.UtcNow -
            TimeSpan.FromMilliseconds(EngineIoProtocol.PingIntervalMs + EngineIoProtocol.PingTimeoutMs);
        foreach ((string sid, SocketIoSession session) in sessions)
        {
            if (session.LastSeen < cutoff)
            {
                RemoveSession(sid);
            }
        }
    }

    public void Dispose()
    {
        lock (gate)
        {
            disposed = true;
            reaper.Dispose();
            statusTimer?.Dispose();
            statusTimer = null;
            encodeTimer?.Dispose();
            encodeTimer = null;
        }

        foreach (SocketIoSession session in sessions.Values)
        {
            session.Complete();
        }

        sessions.Clear();
    }
}

/// <summary>
/// 接続 1 本分。転送方式 (long-polling / WebSocket) が切り替わっても同じ待ち行列を使う。
/// Engine.IO の upgrade は「同じ sid のまま転送方式だけ差し替える」ものなので、
/// 送信待ちのパケットを転送方式ごとに持つと、切り替えの瞬間に取りこぼす。
/// </summary>
public sealed class SocketIoSession : IDisposable
{
    private readonly Channel<string> outbound = Channel.CreateUnbounded<string>(
        new UnboundedChannelOptions { SingleReader = false, SingleWriter = false });
    private readonly Timer pingTimer;

    public SocketIoSession(string sid)
    {
        Sid = sid;
        pingTimer = new Timer(
            _ => Enqueue(EngineIoProtocol.Ping),
            null,
            TimeSpan.FromMilliseconds(EngineIoProtocol.PingIntervalMs),
            TimeSpan.FromMilliseconds(EngineIoProtocol.PingIntervalMs));
    }

    public string Sid { get; }

    /// <summary>socket.io の名前空間 "/" へ接続済みか (<c>40</c> を受け取ったか)。</summary>
    public bool IsConnected { get; set; }

    public bool IsUpgraded { get; set; }

    public DateTimeOffset LastSeen { get; set; } = DateTimeOffset.UtcNow;

    public void Enqueue(string packet) => outbound.Writer.TryWrite(packet);

    public ValueTask<string> ReadAsync(CancellationToken cancellationToken) =>
        outbound.Reader.ReadAsync(cancellationToken);

    public bool TryRead(out string packet) => outbound.Reader.TryRead(out packet!);

    public void Complete()
    {
        Dispose();
        outbound.Writer.TryComplete();
    }

    public void Dispose() => pingTimer.Dispose();
}
