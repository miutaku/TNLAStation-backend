namespace TNLAStation.Application.Abstractions;

/// <summary>
/// ライブ視聴。チューナーは有限なので、開いたセッションは明示的に止めるか、keep が
/// 途切れた時点で回収する。見ている人がいなくなった配信を残すとチューナーが空かない。
/// </summary>
public interface ILiveStreamService
{
    /// <summary>
    /// HLS の配信を始め、stream id を返す。プレイリストは <c>/streamfiles/stream{id}.m3u8</c>。
    /// </summary>
    ValueTask<long> StartHlsAsync(long channelId, int mode, CancellationToken cancellationToken);

    /// <summary>LL-HLS の配信を始める。プレイリストは外部の配信サーバー上なので URL も返す。</summary>
    ValueTask<LowLatencyPlayback> StartLowLatencyAsync(long channelId, int mode, CancellationToken cancellationToken);

    /// <summary>
    /// 録画済みの HLS 配信を始める。ブラウザーは録った MPEG-2 をそのままでは再生できない。
    /// </summary>
    ValueTask<long> StartRecordedHlsAsync(
        long videoFileId,
        double playPosition,
        int mode,
        CancellationToken cancellationToken);

    /// <summary>
    /// 視聴継続の合図。知らない stream id なら false。
    /// </summary>
    bool Keep(long streamId);

    /// <summary>
    /// 配信を止める。既に無い stream id なら false。
    /// </summary>
    ValueTask<bool> StopAsync(long streamId);

    /// <summary>
    /// 開いている配信をすべて畳む。掴んだままのチューナーを手で解放する最後の手段。
    /// </summary>
    ValueTask StopAllAsync();

    /// <summary>
    /// 変換しながら 1 本の流れとして配る。途中のファイルを作らないので遅れは小さいが、
    /// 途中から見ることも巻き戻すこともできない。
    /// </summary>
    ValueTask<TranscodedOutput> OpenTranscodedLiveAsync(
        long channelId,
        string format,
        int mode,
        CancellationToken cancellationToken);

    /// <inheritdoc cref="OpenTranscodedLiveAsync"/>
    ValueTask<TranscodedOutput> OpenTranscodedRecordedAsync(
        long videoFileId,
        string format,
        int mode,
        double playPosition,
        CancellationToken cancellationToken);

    /// <summary>
    /// 放送をそのまま流す。変換しないので画質も落ちず負荷もかからないが、再生できる機器は
    /// 限られる。呼び出し側が閉じるまでチューナーを占有する。
    /// </summary>
    ValueTask<Stream> OpenLiveStreamAsync(long channelId, int mode, CancellationToken cancellationToken);
}

/// <summary>
/// 変換しながら配る 1 本。読み手が閉じたら変換も止まる。
/// </summary>
public sealed record TranscodedOutput(Stream Content, string ContentType);

/// <summary>keep と停止は StreamId、再生は PlaylistUrl。</summary>
public sealed record LowLatencyPlayback(long StreamId, string PlaylistUrl);

/// <summary>
/// 指定したチャンネルが存在しない、チューナーが空いていない、といった視聴を始められない理由。
/// </summary>
public sealed class LiveStreamException(string message) : Exception(message);
