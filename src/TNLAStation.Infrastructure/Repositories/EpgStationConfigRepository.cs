using Microsoft.Extensions.Options;
using TNLAStation.Application.Abstractions;
using TNLAStation.Domain;
using TNLAStation.Infrastructure.Configuration;
using TNLAStation.Infrastructure.Configuration.EpgStation;
using TNLAStation.Infrastructure.Streaming;

namespace TNLAStation.Infrastructure.Repositories;

/// <summary>
/// <c>GET /api/config</c> の中身を、実際に読み込んだ設定から組み立てる。
///
/// 対応する EPGStation 実装は <c>EPGStation/src/model/api/config/ConfigApiModel.ts</c> の
/// <c>getConfig(isSecure)</c> (v2.10.0)。分岐と「出す/出さない」の条件を 1 行ずつ写している。
/// とくに次の 3 点は schema (<c>api.yml</c>) と実装が食い違うところで、実装側に合わせてある:
/// ライブ配信フラグは <c>isEnableTSLiveStream</c>、ライブの形式は <c>streamConfig.live.ts</c> 以下、
/// <c>m2ts</c> だけが <c>{ name, isUnconverted }</c> の組で他は名前の配列。
/// </summary>
public sealed class EpgStationConfigRepository(
    IEpgStationConfigAccessor accessor,
    IBroadcastStatusProvider broadcast,
    IOptions<StreamingOptions> streaming) : IConfigRepository
{
    private readonly StreamingOptions streaming = streaming.Value;


    /// <summary>クライアントへ知らせる socket.io のポートが決められない。EPGStation は throw する。</summary>
    public sealed class ConfigException(string message) : Exception(message);

    public async ValueTask<StationConfiguration> GetAsync(bool isSecure, CancellationToken cancellationToken)
    {
        EpgStationConfigFile config = accessor.Current;

        int socketIoPort = ResolveSocketIoPort(config, isSecure);

        BroadcastAvailability availability = await broadcast.GetAsync(cancellationToken);

        return new StationConfiguration(
            SocketIoPort: socketIoPort,
            Broadcast: availability,
            RecordedDirectories: [.. config.Recorded.Select(directory => directory.Name)],
            EncodeModes: [.. config.Encode.Select(mode => mode.Name)],
            UrlScheme: BuildUrlScheme(config),
            // EPGStation は false で初期化し、対応する stream 設定があるときだけ true にする。
            IsEnableTsLiveStream: config.Stream?.Live?.Ts is not null,
            IsEnableTsRecordedStream: config.Stream?.Recorded?.Ts is not null,
            IsEnableEncodedRecordedStream: config.Stream?.Recorded?.Encoded is not null,
            KodiHosts: config.KodiHosts is null ? null : [.. config.KodiHosts.Select(host => host.Name)],
            StreamConfig: BuildStreamConfig(config, LowLatencyModes()));
    }

    /// <summary>
    /// EPGStation の分岐そのまま。clientSocketioPort があれば無条件にそれ、無ければ https/http の
    /// アクセス種別ごとに socketioPort → port の順で解決する。
    /// </summary>
    internal static int ResolveSocketIoPort(EpgStationConfigFile config, bool isSecure)
    {
        if (config.ClientSocketioPort is { } clientPort)
        {
            return clientPort;
        }

        if (isSecure)
        {
            if (config.Https is null)
            {
                throw new ConfigException("httpsConfigError");
            }

            return config.Https.SocketioPort ?? config.Https.Port ?? throw new ConfigException("httpsConfigError");
        }

        if (config.Port is null)
        {
            throw new ConfigException("httpConfigError");
        }

        return config.SocketioPort ?? config.Port.Value;
    }

    /// <summary>
    /// EPGStation は <c>config.urlscheme.m2ts.ios</c> のように無条件で辿る。config.yml が urlscheme を
    /// 部分的にしか書いていない場合は EPGStation でも TypeError になり 500 が返るので、その形を写す。
    /// </summary>
    private static UrlSchemeConfiguration BuildUrlScheme(EpgStationConfigFile config)
    {
        EpgStationUrlSchemeConfig? scheme = config.UrlScheme
            ?? throw new ConfigException("Cannot read properties of undefined (reading 'm2ts')");

        return new UrlSchemeConfiguration(
            ToInfo(scheme.M2Ts, "m2ts"),
            ToInfo(scheme.Video, "video"),
            ToInfo(scheme.Download, "download"));
    }

    private static UrlSchemeInfo ToInfo(EpgStationUrlSchemeInfo? info, string name) => info is null
        ? throw new ConfigException($"Cannot read properties of undefined (reading '{name}')")
        : new UrlSchemeInfo(info.Ios, info.Android, info.Mac, info.Win);


    /// <summary>
    /// LL-HLS は config.yml ではなく配信サーバーの設定で決まる。画質は HLS と同じものを使う —
    /// 実処理 (RemoteLiveStreamService) がそちらを見ているので、ずらすと選べない選択肢が出る。
    /// </summary>
    private IReadOnlyList<string>? LowLatencyModes() =>
        streaming.LowLatencyHls?.PlaylistUrlTemplate is { Length: > 0 }
            ? [.. (streaming.LiveModes ?? StreamingDefaults.LiveModes).Select(mode => mode.Name)]
            : null;

    private static StreamConfiguration BuildStreamConfig(EpgStationConfigFile config, IReadOnlyList<string>? lowLatencyModes)
    {
        if (config.Stream is null)
        {
            return new StreamConfiguration(Live: null, Recorded: null);
        }

        LiveStreamConfiguration? live = null;
        if (config.Stream.Live is not null)
        {
            TransportStreamConfiguration? ts = null;
            if (config.Stream.Live.Ts is { } liveTs)
            {
                ts = new TransportStreamConfiguration(
                    M2Ts: liveTs.M2Ts is null
                        ? null
                        : [.. liveTs.M2Ts.Select(cmd => new M2TsStreamParameter(cmd.Name, cmd.Cmd is null))],
                    M2TsLl: Names(liveTs.M2TsLl),
                    Webm: Names(liveTs.Webm),
                    Mp4: Names(liveTs.Mp4),
                    Hls: Names(liveTs.Hls),
                    LowLatency: lowLatencyModes);
            }

            live = new LiveStreamConfiguration(ts);
        }

        RecordedStreamConfiguration? recorded = null;
        if (config.Stream.Recorded is not null)
        {
            recorded = new RecordedStreamConfiguration(
                Ts: ToModes(config.Stream.Recorded.Ts),
                Encoded: ToModes(config.Stream.Recorded.Encoded));
        }

        return new StreamConfiguration(live, recorded);
    }

    private static RecordedStreamModes? ToModes(EpgStationRecordedTsConfig? modes) => modes is null
        ? null
        : new RecordedStreamModes(Names(modes.Webm), Names(modes.Mp4), Names(modes.Hls));

    private static IReadOnlyList<string>? Names(IReadOnlyList<EpgStationStreamingCmd>? cmds) =>
        cmds is null ? null : [.. cmds.Select(cmd => cmd.Name)];
}
