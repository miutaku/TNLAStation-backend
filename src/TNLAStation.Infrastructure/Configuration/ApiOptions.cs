namespace TNLAStation.Infrastructure.Configuration;

/// <summary>
/// リバースプロキシのサブパス配下で公開する場合や、フロントエンドを別オリジンから叩く場合の設定。
/// どちらも既定では使わない (直下で単一オリジン運用)。
/// </summary>
public sealed class ApiOptions
{
    public const string SectionName = "Api";

    /// <summary>
    /// リバースプロキシの後ろで `/tnla` のようなサブパス配下に置く場合のプレフィックス。
    /// 先頭は `/` で始める。指定が無ければルート直下で動作する (EPGStation の subDirectory 相当)。
    /// </summary>
    public string? SubDirectory { get; init; }

    /// <summary>
    /// 任意のオリジンからの API 呼び出しを許可するか。既定では許可しない
    /// (同一オリジンのフロントエンドからのみ呼ばれる想定のため)。
    /// </summary>
    public bool IsAllowAllCors { get; init; }

    /// <summary>
    /// OpenAPI ドキュメントの `servers` に載せる URL。API サーバーがリバースプロキシの
    /// 背後にあり、実際の外部到達 URL を明示したい場合に使う。
    /// </summary>
    public IReadOnlyList<string> Servers { get; init; } = [];
}
