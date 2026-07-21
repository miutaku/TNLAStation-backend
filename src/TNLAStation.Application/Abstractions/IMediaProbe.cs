namespace TNLAStation.Application.Abstractions;

/// <summary>
/// 動画ファイルの中身を調べる。長さは録画の行には持たない。録画中に落ちた場合、
/// 予定の長さと実際に録れた長さが食い違うので、ファイルへ聞くのが確かめる唯一の方法。
/// </summary>
public interface IMediaProbe
{
    /// <summary>調べられなければ null。壊れたファイルもあるので、失敗は例外にしない。</summary>
    ValueTask<double?> GetDurationSecondsAsync(string path, CancellationToken cancellationToken);
}
