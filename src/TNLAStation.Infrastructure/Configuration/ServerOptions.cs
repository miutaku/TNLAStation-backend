namespace TNLAStation.Infrastructure.Configuration;

/// <summary>
/// EPGStation の <c>port</c> / <c>socketioPort</c> / <c>clientSocketioPort</c> / <c>https</c> /
/// <c>uid</c> / <c>gid</c> に対応する。
///
/// <c>socketioPort</c> は「socket.io を待ち受けるポート」、<c>clientSocketioPort</c> は
/// 「<c>/api/config</c> の <c>socketIOPort</c> としてクライアントへ知らせるポート」で意味が違う。
/// 上流の <c>ServiceServer.start</c> と <c>ConfigApiModel.getConfig</c> がその使い分けの根拠。
/// </summary>
public sealed class ServerOptions
{
    public const string SectionName = "Server";

    public int Port { get; init; }

    /// <summary>未設定なら <see cref="Port"/> と同じ待受に相乗りする。</summary>
    public int? SocketIoPort { get; init; }

    /// <summary>
    /// 設定すると <c>/api/config</c> はこの値を返す。リバースプロキシ越しに別ポートを見せる構成用。
    /// </summary>
    public int? ClientSocketIoPort { get; init; }

    public ServerHttpsOptions? Https { get; init; }

    public string? Uid { get; init; }

    public string? Gid { get; init; }
}

public sealed class ServerHttpsOptions
{
    public int? Port { get; init; }

    public string? Key { get; init; }

    public string? Cert { get; init; }

    public IReadOnlyList<string>? Ca { get; init; }

    public int? SocketIoPort { get; init; }
}
