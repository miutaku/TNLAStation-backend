using System.Globalization;
using Microsoft.Extensions.Configuration;

namespace TNLAStation.Infrastructure.Configuration.EpgStation;

/// <summary>
/// EPGStation の config.yml を <see cref="IConfiguration"/> の一段として読み込む。
///
/// これで既存の <c>Api:</c>/<c>Storage:</c>… といった Options 束縛はそのまま動き、
/// appsettings 形式は後方互換の入力として残る (後から足した source が勝つので、
/// config.yml を指定した場合は config.yml が優先される)。
/// EPGStation と同じくファイルを監視し、書き換わったら読み直して
/// <see cref="IOptionsMonitor{T}"/> 経由で反映する。
/// </summary>
public sealed class EpgStationConfigurationSource : IConfigurationSource
{
    public required string ConfigPath { get; init; }

    public required string RootPath { get; init; }

    public string? TemplatePath { get; init; }

    /// <summary>ファイルが無いときに例外にするか。</summary>
    public bool Optional { get; init; }

    public bool ReloadOnChange { get; init; } = true;

    public IConfigurationProvider Build(IConfigurationBuilder builder) => new EpgStationConfigurationProvider(this);
}

public sealed class EpgStationConfigurationProvider : ConfigurationProvider, IDisposable
{
    /// <summary>
    /// Node の <c>fs.watchFile</c> の既定間隔 (5007ms) に合わせた見に行く間隔。上流も
    /// inotify ではなく stat の定期取得で config.yml の変化を拾っている。
    /// </summary>
    internal static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(5007);

    private readonly EpgStationConfigurationSource source;
    private readonly Timer? watcher;
    private DateTime lastWriteUtc;
    private long lastLength = -1;

    public EpgStationConfigurationProvider(EpgStationConfigurationSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        this.source = source;

        if (!source.ReloadOnChange)
        {
            return;
        }

        watcher = new Timer(_ => PollForChanges(), null, PollInterval, PollInterval);
    }

    /// <summary>読み込んだ設定そのもの。<c>/api/config</c> はこれを見る。</summary>
    public EpgStationConfigFile? Config { get; private set; }

    public override void Load()
    {
        if (!File.Exists(source.ConfigPath))
        {
            if (!source.Optional)
            {
                throw new FileNotFoundException($"{source.ConfigPath} is not found", source.ConfigPath);
            }

            Config = null;
            Data = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            return;
        }

        EpgStationConfigFile config = EpgStationConfigLoader.Load(
            source.ConfigPath,
            source.RootPath,
            source.TemplatePath is not null && File.Exists(source.TemplatePath) ? source.TemplatePath : null);

        Config = config;
        Data = EpgStationOptionMapper.ToConfigurationData(config);
        RememberStamp();
    }

    private void RememberStamp()
    {
        var info = new FileInfo(source.ConfigPath);
        lastWriteUtc = info.Exists ? info.LastWriteTimeUtc : default;
        lastLength = info.Exists ? info.Length : -1;
    }

    /// <summary>
    /// 更新時刻か大きさが変わっていたら読み直す。読み直しに失敗した場合は、上流と同じく
    /// 直前に読めた設定のまま動き続ける (起動中のサーバーを設定の書き損じで止めない)。
    /// </summary>
    internal void PollForChanges()
    {
        try
        {
            var info = new FileInfo(source.ConfigPath);
            DateTime writeUtc = info.Exists ? info.LastWriteTimeUtc : default;
            long length = info.Exists ? info.Length : -1;
            if (writeUtc == lastWriteUtc && length == lastLength)
            {
                return;
            }

            Load();
            OnReload();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or EpgStationConfigLoader.PortSettingException or YamlDotNet.Core.YamlException)
        {
            RememberStamp();
        }
    }

    public void Dispose() => watcher?.Dispose();
}

public static class EpgStationConfigurationBuilderExtensions
{
    /// <summary>
    /// EPGStation 形式の config.yml を読み込む。<paramref name="rootPath"/> を省略すると
    /// config.yml の親の親 (EPGStation の <c>%ROOT%</c> にあたる場所) を使う。
    /// </summary>
    public static IConfigurationBuilder AddEpgStationConfigFile(
        this IConfigurationBuilder builder,
        string configPath,
        string? rootPath = null,
        bool optional = true,
        bool reloadOnChange = true)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrEmpty(configPath);

        string fullPath = Path.GetFullPath(configPath);
        string configDirectory = Path.GetDirectoryName(fullPath) ?? Directory.GetCurrentDirectory();
        string root = rootPath ?? Path.GetDirectoryName(configDirectory) ?? configDirectory;

        return builder.Add(new EpgStationConfigurationSource
        {
            ConfigPath = fullPath,
            RootPath = root.TrimEnd(Path.DirectorySeparatorChar),
            TemplatePath = Path.Combine(configDirectory, "config.yml.template"),
            Optional = optional,
            ReloadOnChange = reloadOnChange,
        });
    }
}

/// <summary>
/// EPGStation の config キーを TNLAStation の Options キーへ写す。対応表は
/// <c>docs/compatibility.md</c> の「config 対応表」と 1 対 1 で対応する。
/// </summary>
internal static class EpgStationOptionMapper
{
    public static Dictionary<string, string?> ToConfigurationData(EpgStationConfigFile config)
    {
        var data = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        void Set(string key, string? value)
        {
            if (value is not null)
            {
                data[key] = value;
            }
        }

        void SetInt(string key, int value) => data[key] = value.ToString(CultureInfo.InvariantCulture);
        void SetBool(string key, bool value) => data[key] = value ? "true" : "false";

        // --- API / 公開面 ---
        Set("Api:SubDirectory", config.SubDirectory);
        SetBool("Api:IsAllowAllCors", config.IsAllowAllCors);
        for (int i = 0; i < config.ApiServers.Count; i++)
        {
            Set($"Api:Servers:{i}", config.ApiServers[i]);
        }

        SetInt("Server:Port", config.Port ?? 0);
        if (config.SocketioPort is { } socketioPort)
        {
            SetInt("Server:SocketIoPort", socketioPort);
        }

        if (config.ClientSocketioPort is { } clientSocketioPort)
        {
            SetInt("Server:ClientSocketIoPort", clientSocketioPort);
        }

        if (config.Https is { } https)
        {
            if (https.Port is { } httpsPort)
            {
                SetInt("Server:Https:Port", httpsPort);
            }

            Set("Server:Https:Key", https.Key);
            Set("Server:Https:Cert", https.Cert);
            if (https.SocketioPort is { } httpsSocketioPort)
            {
                SetInt("Server:Https:SocketIoPort", httpsSocketioPort);
            }

            if (https.Ca is { } ca)
            {
                for (int i = 0; i < ca.Count; i++)
                {
                    Set($"Server:Https:Ca:{i}", ca[i]);
                }
            }
        }

        Set("Server:Uid", config.Uid);
        Set("Server:Gid", config.Gid);

        // --- Mirakurun ---
        Set("Mirakurun:BaseUrl", config.MirakurunPath);
        SetInt("Mirakurun:RecPriority", config.RecPriority);
        SetInt("Mirakurun:ConflictPriority", config.ConflictPriority);
        SetInt("Mirakurun:StreamingPriority", config.StreamingPriority);

        // --- EPG ---
        SetBool("Epg:NeedToReplaceEnclosingCharacters", config.NeedToReplaceEnclosingCharacters);
        SetInt("Epg:UpdateIntervalMinutes", config.EpgUpdateIntervalTime);
        WriteList(data, "Epg:ChannelOrder", config.ChannelOrder);
        WriteList(data, "Epg:SidOrder", config.SidOrder);
        WriteList(data, "Epg:ExcludeChannels", config.ExcludeChannels);
        WriteList(data, "Epg:ExcludeSids", config.ExcludeSids);

        // --- DB ---
        if (config.Postgres is { } postgres)
        {
            data["ConnectionStrings:PostgreSQL"] =
                $"Host={postgres.Host};Port={postgres.Port.ToString(CultureInfo.InvariantCulture)};" +
                $"Database={postgres.Database};Username={postgres.User};Password={postgres.Password}";
        }

        Set("Database:Type", config.DbType);

        // --- 録画 ---
        SetInt("Recording:StartMarginSeconds", config.TimeSpecifiedStartMargin);
        SetInt("Recording:EndMarginSeconds", config.TimeSpecifiedEndMargin);
        Set("Recording:RecordedFormat", config.RecordedFormat);
        Set("Recording:RecordedFileExtension", config.RecordedFileExtension);
        Set("Recording:TempDirectory", config.RecordedTmp);
        Set("Recording:DropLogDirectory", config.DropLog);
        SetBool("Recording:IsEnabledDropCheck", config.IsEnabledDropCheck);

        // --- 予約 ---
        SetInt("Reserve:RecordedHistoryRetentionPeriodDays", config.RecordedHistoryRetentionPeriodDays);
        SetBool("Reserve:IsSuppressReservesUpdateAllLog", config.IsSuppressReservesUpdateAllLog);

        // --- 保存先 ---
        for (int i = 0; i < config.Recorded.Count; i++)
        {
            EpgStationRecordedDirInfo directory = config.Recorded[i];
            Set($"Storage:RecordedDirectories:{i}:Name", directory.Name);
            Set($"Storage:RecordedDirectories:{i}:Path", directory.Path);
            if (directory.LimitThreshold is { } threshold)
            {
                data[$"Storage:RecordedDirectories:{i}:LimitThresholdMb"] =
                    threshold.ToString(CultureInfo.InvariantCulture);
            }

            Set($"Storage:RecordedDirectories:{i}:Action", directory.Action);
            Set($"Storage:RecordedDirectories:{i}:LimitCmd", directory.LimitCmd);
        }

        SetInt("Storage:StorageLimitCheckIntervalSeconds", config.StorageLimitCheckIntervalTime);
        Set("Storage:UploadTempDirectory", config.UploadTempDir);

        // --- サムネイル ---
        Set("Thumbnail:Directory", config.Thumbnail);
        (int width, int? height) = ParseThumbnailSize(config.ThumbnailSize);
        SetInt("Thumbnail:Width", width);
        if (height is { } thumbnailHeight)
        {
            SetInt("Thumbnail:Height", thumbnailHeight);
        }

        data["Thumbnail:PositionSeconds"] = config.ThumbnailPosition.ToString(CultureInfo.InvariantCulture);
        Set("Thumbnail:Command", config.ThumbnailCmd);

        // --- ffmpeg / エンコード ---
        // backend と ffmpeg-worker は同じ configuration document を読む。worker 側の
        // FfmpegOptions の実プロパティ名へも写さないと、config.yml に指定したバイナリと
        // プロセス上限が実際にプロセスを起動する側へ届かない。
        Set("Ffmpeg:FfmpegPath", config.Ffmpeg);
        Set("Ffmpeg:FfprobePath", config.Ffprobe);
        SetInt("Ffmpeg:EncodeProcessNum", config.EncodeProcessNum);
        SetInt("Encode:ProcessNum", config.EncodeProcessNum);
        SetInt("Encode:ConcurrentEncodeNum", config.ConcurrentEncodeNum);
        for (int i = 0; i < config.Encode.Count; i++)
        {
            EpgStationEncodeInfo mode = config.Encode[i];
            Set($"Encode:Modes:{i}:Name", mode.Name);
            Set($"Encode:Modes:{i}:Command", mode.Cmd);
            Set($"Encode:Modes:{i}:Extension", mode.Suffix);
            if (mode.Rate is { } rate)
            {
                data[$"Encode:Modes:{i}:RateTimeoutMultiplier"] = rate.ToString(CultureInfo.InvariantCulture);
            }
        }

        // --- コマンドフック ---
        Set("CommandHooks:ReserveNewAdditionCommand", config.ReserveNewAddtionCommand);
        Set("CommandHooks:ReserveUpdateCommand", config.ReserveUpdateCommand);
        Set("CommandHooks:ReserveDeletedCommand", config.ReservedeletedCommand);
        Set("CommandHooks:RecordingPreStartCommand", config.RecordingPreStartCommand);
        Set("CommandHooks:RecordingPrepRecFailedCommand", config.RecordingPrepRecFailedCommand);
        Set("CommandHooks:RecordingStartCommand", config.RecordingStartCommand);
        Set("CommandHooks:RecordingFinishCommand", config.RecordingFinishCommand);
        Set("CommandHooks:RecordingFailedCommand", config.RecordingFailedCommand);
        Set("CommandHooks:EncodingFinishCommand", config.EncodingFinishCommand);

        // --- URL scheme ---
        WriteUrlScheme(data, "UrlScheme:M2Ts", config.UrlScheme?.M2Ts);
        WriteUrlScheme(data, "UrlScheme:Video", config.UrlScheme?.Video);
        WriteUrlScheme(data, "UrlScheme:Download", config.UrlScheme?.Download);

        // --- 配信 ---
        Set("Streaming:WorkDirectory", config.StreamFilePath);

        // --- kodi ---
        if (config.KodiHosts is { } kodiHosts)
        {
            for (int i = 0; i < kodiHosts.Count; i++)
            {
                EpgStationKodiInfo host = kodiHosts[i];
                Set($"Kodi:Hosts:{i}:Name", host.Name);
                Set($"Kodi:Hosts:{i}:Url", ResolveJsonRpcUrl(host.Host));
                Set($"Kodi:Hosts:{i}:User", host.User);
                Set($"Kodi:Hosts:{i}:Password", host.Password);
            }
        }

        return data;
    }

    /// <summary>
    /// EPGStation は <c>url.resolve(kodiInfo.host, '/jsonrpc')</c> で送信先を作る。origin 直下の
    /// <c>/jsonrpc</c> になるので、host にパスが付いていても捨てられる。
    /// </summary>
    private static string ResolveJsonRpcUrl(string host) =>
        Uri.TryCreate(host, UriKind.Absolute, out Uri? uri)
            ? new Uri(uri, "/jsonrpc").ToString()
            : host;

    /// <summary>
    /// <c>thumbnailSize</c> は <c>幅x高さ</c>。分解できない場合は既定の 480x270 として扱う。
    /// </summary>
    internal static (int Width, int? Height) ParseThumbnailSize(string size)
    {
        string[] parts = size.Split('x', 'X');
        if (parts.Length == 2 &&
            int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int width) &&
            int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int height))
        {
            return (width, height);
        }

        return (480, 270);
    }

    private static void WriteList<T>(Dictionary<string, string?> data, string prefix, IReadOnlyList<T>? values)
        where T : struct, IFormattable
    {
        if (values is null)
        {
            return;
        }

        for (int i = 0; i < values.Count; i++)
        {
            data[$"{prefix}:{i}"] = values[i].ToString(null, CultureInfo.InvariantCulture);
        }
    }

    private static void WriteUrlScheme(
        Dictionary<string, string?> data,
        string prefix,
        EpgStationUrlSchemeInfo? scheme)
    {
        if (scheme is null)
        {
            return;
        }

        if (scheme.Ios is not null)
        {
            data[$"{prefix}:Ios"] = scheme.Ios;
        }

        if (scheme.Android is not null)
        {
            data[$"{prefix}:Android"] = scheme.Android;
        }

        if (scheme.Mac is not null)
        {
            data[$"{prefix}:Mac"] = scheme.Mac;
        }

        if (scheme.Win is not null)
        {
            data[$"{prefix}:Win"] = scheme.Win;
        }
    }
}
