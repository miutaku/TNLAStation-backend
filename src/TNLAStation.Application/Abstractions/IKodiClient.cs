namespace TNLAStation.Application.Abstractions;

/// <summary>
/// 手元の画面ではなく、テレビに繋がった Kodi で再生させる。送るのは URL だけで、
/// 中身は Kodi が取りに来る。
/// </summary>
public interface IKodiClient
{
    /// <summary>設定されている送り先の名前。</summary>
    IReadOnlyList<string> HostNames { get; }

    /// <summary>
    /// 渡す URL の前半を固定する設定。無ければ、操作した人が使ったアドレスをそのまま使う。
    /// </summary>
    string? PublicBaseUrl { get; }

    /// <summary>
    /// 再生を頼む。名前に合う送り先が無ければ false。届いたかどうかまでは分からないので、
    /// 応答が返ったことをもって成功とする。
    /// </summary>
    ValueTask<bool> PlayAsync(string hostName, string url, CancellationToken cancellationToken);
}
