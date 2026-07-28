using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using TNLAStation.Infrastructure.Mirakurun;

namespace TNLAStation.Infrastructure.Configuration.EpgStation;

/// <summary>
/// いま有効な EPGStation 形式の設定を配る。
///
/// config.yml を読み込んでいればその内容をそのまま、読み込んでいなければ (appsettings 形式だけの
/// 後方互換構成) 各 Options から同じ形へ組み立てて返す。<c>/api/config</c> はどちらの構成でも
/// 同じ経路で応答できる。
/// </summary>
public interface IEpgStationConfigAccessor
{
    EpgStationConfigFile Current { get; }

    /// <summary>config.yml から読んだ設定かどうか。</summary>
    bool IsFromConfigFile { get; }
}

public sealed class EpgStationConfigAccessor(
    IConfiguration configuration,
    IOptionsMonitor<ApiOptions> api,
    IOptionsMonitor<StorageOptions> storage,
    IOptionsMonitor<EncodeOptions> encode,
    IOptionsMonitor<KodiOptions> kodi,
    IOptionsMonitor<UrlSchemeOptions> urlScheme,
    IOptionsMonitor<StreamingOptions> streaming,
    IOptionsMonitor<ThumbnailOptions> thumbnail,
    IOptionsMonitor<RecordingOptions> recording,
    IOptionsMonitor<ReserveOptions> reserve,
    IOptionsMonitor<EpgOptions> epg,
    IOptionsMonitor<MirakurunOptions> mirakurun,
    IOptionsMonitor<CommandHookOptions> hooks,
    IOptionsMonitor<ServerOptions> server) : IEpgStationConfigAccessor
{
    private EpgStationConfigurationProvider? FileProvider => (configuration as IConfigurationRoot)?.Providers
        .OfType<EpgStationConfigurationProvider>()
        .FirstOrDefault(provider => provider.Config is not null);

    public bool IsFromConfigFile => FileProvider is not null;

    public EpgStationConfigFile Current => FileProvider?.Config ?? BuildFromOptions();

    /// <summary>
    /// appsettings 形式だけで動かしている構成のための組み立て。config.yml を置けば
    /// こちらは使われない。
    /// </summary>
    private EpgStationConfigFile BuildFromOptions()
    {
        StreamingOptions streamingOptions = streaming.CurrentValue;
        ThumbnailOptions thumbnailOptions = thumbnail.CurrentValue;

        return new EpgStationConfigFile
        {
            Port = server.CurrentValue.Port == 0 ? null : server.CurrentValue.Port,
            SocketioPort = server.CurrentValue.SocketIoPort,
            ClientSocketioPort = server.CurrentValue.ClientSocketIoPort,
            MirakurunPath = mirakurun.CurrentValue.BaseUrl ?? EpgStationConfigLoader.DefaultMirakurunPath,
            SubDirectory = api.CurrentValue.SubDirectory,
            ApiServers = api.CurrentValue.Servers.Count > 0
                ? api.CurrentValue.Servers
                : [$"http://localhost:{server.CurrentValue.Port}"],
            IsAllowAllCors = api.CurrentValue.IsAllowAllCors,
            DbType = "postgres",
            NeedToReplaceEnclosingCharacters = epg.CurrentValue.NeedToReplaceEnclosingCharacters,
            EpgUpdateIntervalTime = epg.CurrentValue.UpdateIntervalMinutes,
            ChannelOrder = epg.CurrentValue.ChannelOrder,
            SidOrder = epg.CurrentValue.SidOrder,
            ExcludeChannels = epg.CurrentValue.ExcludeChannels,
            ExcludeSids = epg.CurrentValue.ExcludeSids,
            RecPriority = mirakurun.CurrentValue.RecPriority,
            ConflictPriority = mirakurun.CurrentValue.ConflictPriority,
            StreamingPriority = mirakurun.CurrentValue.StreamingPriority,
            TimeSpecifiedStartMargin = recording.CurrentValue.StartMarginSeconds,
            TimeSpecifiedEndMargin = recording.CurrentValue.EndMarginSeconds,
            RecordedFormat = recording.CurrentValue.RecordedFormat,
            RecordedFileExtension = recording.CurrentValue.RecordedFileExtension,
            Recorded =
            [
                .. storage.CurrentValue.RecordedDirectories.Select(directory => new EpgStationRecordedDirInfo
                {
                    Name = directory.Name,
                    Path = directory.Path,
                    LimitThreshold = directory.LimitThresholdMb,
                    Action = directory.Action,
                    LimitCmd = directory.LimitCmd,
                }),
            ],
            RecordedTmp = recording.CurrentValue.TempDirectory,
            RecordedHistoryRetentionPeriodDays = reserve.CurrentValue.RecordedHistoryRetentionPeriodDays,
            StorageLimitCheckIntervalTime = storage.CurrentValue.StorageLimitCheckIntervalSeconds,
            Thumbnail = thumbnailOptions.Directory,
            ThumbnailCmd = thumbnailOptions.Command ?? EpgStationConfigLoader.DefaultThumbnailCmd,
            ThumbnailSize = $"{thumbnailOptions.Width}x{thumbnailOptions.Height ?? 270}",
            ThumbnailPosition = (int)thumbnailOptions.PositionSeconds,
            DropLog = recording.CurrentValue.DropLogDirectory ?? string.Empty,
            IsEnabledDropCheck = recording.CurrentValue.IsEnabledDropCheck,
            UploadTempDir = storage.CurrentValue.UploadTempDirectory ?? string.Empty,
            EncodeProcessNum = encode.CurrentValue.ProcessNum,
            ConcurrentEncodeNum = encode.CurrentValue.ConcurrentEncodeNum,
            Encode =
            [
                .. encode.CurrentValue.Modes.Select(mode => new EpgStationEncodeInfo
                {
                    Name = mode.Name,
                    Cmd = mode.Command ?? string.Empty,
                    Suffix = mode.Extension,
                    Rate = mode.RateTimeoutMultiplier,
                }),
            ],
            IsSuppressReservesUpdateAllLog = reserve.CurrentValue.IsSuppressReservesUpdateAllLog,
            ReserveNewAddtionCommand = hooks.CurrentValue.ReserveNewAdditionCommand,
            ReserveUpdateCommand = hooks.CurrentValue.ReserveUpdateCommand,
            ReservedeletedCommand = hooks.CurrentValue.ReserveDeletedCommand,
            RecordingPreStartCommand = hooks.CurrentValue.RecordingPreStartCommand,
            RecordingPrepRecFailedCommand = hooks.CurrentValue.RecordingPrepRecFailedCommand,
            RecordingStartCommand = hooks.CurrentValue.RecordingStartCommand,
            RecordingFinishCommand = hooks.CurrentValue.RecordingFinishCommand,
            RecordingFailedCommand = hooks.CurrentValue.RecordingFailedCommand,
            EncodingFinishCommand = hooks.CurrentValue.EncodingFinishCommand,
            UrlScheme = new EpgStationUrlSchemeConfig
            {
                M2Ts = ToSchemeInfo(urlScheme.CurrentValue.M2Ts),
                Video = ToSchemeInfo(urlScheme.CurrentValue.Video),
                Download = ToSchemeInfo(urlScheme.CurrentValue.Download),
            },
            StreamFilePath = streamingOptions.WorkDirectory,
            Stream = streamingOptions.Stream,
            KodiHosts = kodi.CurrentValue.ConfiguredHosts.Any()
                ? [.. kodi.CurrentValue.ConfiguredHosts.Select(host => new EpgStationKodiInfo
                {
                    Name = host.Name,
                    Host = host.Url,
                    User = host.User,
                    Password = host.Password,
                })]
                : null,
        };
    }

    private static EpgStationUrlSchemeInfo ToSchemeInfo(UrlSchemeEntryOptions entry) => new()
    {
        Ios = entry.Ios,
        Android = entry.Android,
        Mac = entry.Mac,
        Win = entry.Win,
    };
}
