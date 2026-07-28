namespace TNLAStation.Infrastructure.Configuration;

public sealed class KodiOptions
{
    public const string SectionName = "Kodi";

    /// <summary>送り先。名前で選ぶので、名前は設定した人が読んで分かるものにする。</summary>
    public IReadOnlyList<KodiHostOptions> Hosts { get; init; } = [];

    /// <summary>
    /// 実際に使える送り先だけ。設定の口を空のまま残すと空の要素が 1 つできるので、
    /// 名前も宛先も無いものは送り先として数えない。あると画面に選べない項目が出る。
    /// </summary>
    public IEnumerable<KodiHostOptions> ConfiguredHosts => Hosts.Where(host =>
        !string.IsNullOrWhiteSpace(host.Name) && !string.IsNullOrWhiteSpace(host.Url));

    /// <summary>
    /// Kodi へ渡す URL の前半。既定では、操作した人が使ったアドレスをそのまま使う。
    /// サーバー上のブラウザーから操作すると localhost になり、Kodi からは取りに行けないので、
    /// その場合だけここで固定する。
    /// </summary>
    public string? PublicBaseUrl { get; init; }
}

public sealed class KodiHostOptions
{
    public string Name { get; init; } = string.Empty;

    /// <summary>JSON-RPC の口。多くの場合 http://<host>:8080/jsonrpc。</summary>
    public string Url { get; init; } = string.Empty;

    public string? User { get; init; }

    public string? Password { get; init; }
}
