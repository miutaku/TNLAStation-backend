namespace TNLAStation.Infrastructure.Configuration.EpgStation;

/// <summary>
/// EPGStation v2.10.0 の <c>src/model/IConfigFile.ts</c> をそのまま写した形。名前・階層・省略可否まで
/// EPGStation に合わせてある。<c>null</c> は「その項目が config.yml に書かれておらず既定値も無い」という
/// TypeScript の <c>undefined</c> に対応する。
/// </summary>
public sealed class EpgStationConfigFile
{
    public int? Port { get; init; }

    public int? SocketioPort { get; init; }

    public int? ClientSocketioPort { get; init; }

    public EpgStationHttpsConfig? Https { get; init; }

    public string MirakurunPath { get; init; } = string.Empty;

    public string? SubDirectory { get; init; }

    /// <summary>uid は数値でも名前でも書ける。EPGStation は文字列のまま扱う。</summary>
    public string? Uid { get; init; }

    public string? Gid { get; init; }

    public IReadOnlyList<string> ApiServers { get; init; } = [];

    public bool IsAllowAllCors { get; init; }

    public string DbType { get; init; } = "sqlite";

    public EpgStationSqliteConfig? Sqlite { get; init; }

    public EpgStationMySqlConfig? MySql { get; init; }

    public EpgStationPostgresConfig? Postgres { get; init; }

    public bool NeedToReplaceEnclosingCharacters { get; init; } = true;

    public int EpgUpdateIntervalTime { get; init; } = 10;

    public IReadOnlyList<long>? ChannelOrder { get; init; }

    public IReadOnlyList<int>? SidOrder { get; init; }

    public IReadOnlyList<long>? ExcludeChannels { get; init; }

    public IReadOnlyList<int>? ExcludeSids { get; init; }

    public int RecPriority { get; init; } = 2;

    public int ConflictPriority { get; init; } = 1;

    public int StreamingPriority { get; init; }

    public int TimeSpecifiedStartMargin { get; init; } = 1;

    public int TimeSpecifiedEndMargin { get; init; } = 1;

    public string RecordedFormat { get; init; } = string.Empty;

    public string RecordedFileExtension { get; init; } = ".ts";

    public IReadOnlyList<EpgStationRecordedDirInfo> Recorded { get; init; } = [];

    public string? RecordedTmp { get; init; }

    public int RecordedHistoryRetentionPeriodDays { get; init; } = 90;

    public int StorageLimitCheckIntervalTime { get; init; } = 60;

    public string Thumbnail { get; init; } = string.Empty;

    public string ThumbnailCmd { get; init; } = string.Empty;

    public string ThumbnailSize { get; init; } = "480x270";

    public int ThumbnailPosition { get; init; } = 5;

    public string DropLog { get; init; } = string.Empty;

    public bool IsEnabledDropCheck { get; init; }

    public string UploadTempDir { get; init; } = string.Empty;

    public string Ffmpeg { get; init; } = "/usr/local/bin/ffmpeg";

    public string Ffprobe { get; init; } = "/usr/local/bin/ffprobe";

    public int EncodeProcessNum { get; init; }

    public int ConcurrentEncodeNum { get; init; }

    public IReadOnlyList<EpgStationEncodeInfo> Encode { get; init; } = [];

    public bool IsSuppressReservesUpdateAllLog { get; init; }

    public string? ReserveNewAddtionCommand { get; init; }

    public string? ReserveUpdateCommand { get; init; }

    public string? ReservedeletedCommand { get; init; }

    public string? RecordingPreStartCommand { get; init; }

    public string? RecordingPrepRecFailedCommand { get; init; }

    public string? RecordingStartCommand { get; init; }

    public string? RecordingFinishCommand { get; init; }

    public string? RecordingFailedCommand { get; init; }

    public string? EncodingFinishCommand { get; init; }

    /// <summary>
    /// EPGStation の既定値は 3 つとも必ず入る。config.yml が一部だけ書いた場合は書いた分だけになり、
    /// <c>/api/config</c> は EPGStation でも例外 (500) になる。その挙動ごと再現するため null を許す。
    /// </summary>
    public EpgStationUrlSchemeConfig? UrlScheme { get; init; }

    public string StreamFilePath { get; init; } = string.Empty;

    public EpgStationStreamConfig? Stream { get; init; }

    public IReadOnlyList<EpgStationKodiInfo>? KodiHosts { get; init; }
}

public sealed class EpgStationHttpsConfig
{
    public int? Port { get; init; }

    public string? Key { get; init; }

    public string? Cert { get; init; }

    public IReadOnlyList<string>? Ca { get; init; }

    public int? SocketioPort { get; init; }
}

public sealed class EpgStationSqliteConfig
{
    public IReadOnlyList<string>? Extensions { get; init; }

    public bool? Regexp { get; init; }
}

public sealed class EpgStationMySqlConfig
{
    public string Host { get; init; } = string.Empty;

    public string User { get; init; } = string.Empty;

    public int Port { get; init; }

    public string Password { get; init; } = string.Empty;

    public string Database { get; init; } = string.Empty;

    public string? Charset { get; init; }
}

public sealed class EpgStationPostgresConfig
{
    public string Host { get; init; } = string.Empty;

    public string User { get; init; } = string.Empty;

    public int Port { get; init; }

    public string Database { get; init; } = string.Empty;

    public string Password { get; init; } = string.Empty;
}

public sealed class EpgStationRecordedDirInfo
{
    public string Name { get; init; } = string.Empty;

    public string Path { get; init; } = string.Empty;

    public long? LimitThreshold { get; init; }

    public string? Action { get; init; }

    public string? LimitCmd { get; init; }
}

public sealed class EpgStationEncodeInfo
{
    public string Name { get; init; } = string.Empty;

    public string Cmd { get; init; } = string.Empty;

    /// <summary>非エンコードコマンドの場合は未設定。</summary>
    public string? Suffix { get; init; }

    public double? Rate { get; init; }
}

public sealed class EpgStationUrlSchemeConfig
{
    public EpgStationUrlSchemeInfo? M2Ts { get; init; }

    public EpgStationUrlSchemeInfo? Video { get; init; }

    public EpgStationUrlSchemeInfo? Download { get; init; }
}

public sealed class EpgStationUrlSchemeInfo
{
    public string? Ios { get; init; }

    public string? Android { get; init; }

    public string? Mac { get; init; }

    public string? Win { get; init; }
}

public sealed class EpgStationKodiInfo
{
    public string Name { get; init; } = string.Empty;

    public string Host { get; init; } = string.Empty;

    public string? User { get; init; }

    public string? Password { get; init; }
}

/// <summary>
/// 配信コマンド 1 件。<c>Cmd</c> が未設定 (null) だと「無変換」を意味し、
/// <c>/api/config</c> の <c>streamConfig.live.ts.m2ts[].isUnconverted</c> がそこから決まる。
/// </summary>
public sealed class EpgStationStreamingCmd
{
    public string Name { get; init; } = string.Empty;

    public string? Cmd { get; init; }
}

public sealed class EpgStationStreamConfig
{
    public EpgStationLiveStreamConfig? Live { get; init; }

    public EpgStationRecordedStreamConfig? Recorded { get; init; }
}

public sealed class EpgStationLiveStreamConfig
{
    public EpgStationLiveTsConfig? Ts { get; init; }
}

public sealed class EpgStationLiveTsConfig
{
    public IReadOnlyList<EpgStationStreamingCmd>? M2Ts { get; init; }

    public IReadOnlyList<EpgStationStreamingCmd>? M2TsLl { get; init; }

    public IReadOnlyList<EpgStationStreamingCmd>? Webm { get; init; }

    public IReadOnlyList<EpgStationStreamingCmd>? Mp4 { get; init; }

    public IReadOnlyList<EpgStationStreamingCmd>? Hls { get; init; }
}

public sealed class EpgStationRecordedStreamConfig
{
    public EpgStationRecordedTsConfig? Ts { get; init; }

    public EpgStationRecordedTsConfig? Encoded { get; init; }
}

public sealed class EpgStationRecordedTsConfig
{
    public IReadOnlyList<EpgStationStreamingCmd>? Webm { get; init; }

    public IReadOnlyList<EpgStationStreamingCmd>? Mp4 { get; init; }

    public IReadOnlyList<EpgStationStreamingCmd>? Hls { get; init; }
}
