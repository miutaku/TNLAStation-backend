namespace TNLAStation.Infrastructure.Configuration;

/// <summary>
/// ffmpeg/ffprobe を実行する別コンテナ (TNLAStation.FfmpegWorker) の接続先。
/// 用途別URLを省略した場合は、後方互換のため <see cref="BaseUrl"/> を使用する。
/// </summary>
public sealed class FfmpegWorkerOptions
{
    public const string SectionName = "FfmpegWorker";

    public string BaseUrl { get; init; } = string.Empty;

    /// <summary>録画TSのエンコード、probe、サムネイル生成を行うworker pool。</summary>
    public string? EncodeBaseUrl { get; init; }

    /// <summary>ライブ・録画streamを変換するworker pool。</summary>
    public string? StreamingBaseUrl { get; init; }

    /// <summary>
    /// ストリーミングworkerの個別URL。指定時はreadyな接続先を開始要求ごとに巡回する。
    /// </summary>
    public List<string> StreamingBaseUrls { get; init; } = [];

    /// <summary>Kubernetes node名ごとの相対処理能力。</summary>
    public Dictionary<string, double> StreamingNodeWeights { get; init; } = [];

    public string ResolveEncodeBaseUrl() =>
        string.IsNullOrWhiteSpace(EncodeBaseUrl) ? BaseUrl : EncodeBaseUrl;

    public string ResolveStreamingBaseUrl() =>
        string.IsNullOrWhiteSpace(StreamingBaseUrl) ? BaseUrl : StreamingBaseUrl;
}
