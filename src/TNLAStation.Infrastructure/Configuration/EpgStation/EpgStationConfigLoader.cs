using System.Globalization;
using YamlDotNet.RepresentationModel;

namespace TNLAStation.Infrastructure.Configuration.EpgStation;

/// <summary>
/// EPGStation の <c>config/config.yml</c> をそのまま読む。
///
/// 対応する上流実装は <c>EPGStation/src/model/Configuration.ts</c> (v2.10.0,
/// 5cf2ea383d37937eacecf424820dbd7a278d577e) の <c>readConfig</c> / <c>formatConfig</c> /
/// <c>setTemplateValues</c> / <c>directoryFormatting</c>。既定値の表 (<c>DEFAULT_VALUE</c>) と
/// 整形の順序まで写してあるので、同じ config.yml からは同じ結果が出る。
///
/// 「書かれていない」と「書かれているが空」の区別が外から見える (既定値が入るかどうかが変わる) ため、
/// POCO へ一度流し込まず YAML の木のまま鍵の有無を見ている。
/// </summary>
public static class EpgStationConfigLoader
{
    /// <summary>設定の矛盾。EPGStation は <c>PortSettingError</c> を throw して起動を止める。</summary>
    public sealed class PortSettingException() : Exception("PortSettingError");

    /// <summary>
    /// config.yml を読む。
    /// </summary>
    /// <param name="configPath">config.yml のパス。</param>
    /// <param name="rootPath">
    /// <c>%ROOT%</c> の展開先。EPGStation では インストール先 (<c>config/</c> の親) にあたる。
    /// </param>
    /// <param name="templatePath">
    /// config.yml.template のパス。読めない場合、上流と同じく stream の既定値補完を行わない。
    /// </param>
    public static EpgStationConfigFile Load(string configPath, string rootPath, string? templatePath = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(configPath);
        ArgumentNullException.ThrowIfNull(rootPath);

        YamlMappingNode? template = null;
        if (templatePath is not null)
        {
            try
            {
                template = ParseFile(templatePath);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // 上流も template が読めなければ警告だけで続行する。
                template = null;
            }
        }

        return Parse(File.ReadAllText(configPath), rootPath, template);
    }

    /// <summary>YAML 文字列から読む。試験と、config.yml を持たない構成のため。</summary>
    public static EpgStationConfigFile Parse(string yaml, string rootPath, YamlMappingNode? template = null)
    {
        YamlMappingNode config = ParseText(yaml);
        return FormatConfig(config, rootPath, template);
    }

    public static YamlMappingNode ParseFile(string path) => ParseText(File.ReadAllText(path));

    /// <summary>config.yml.template を文字列から読む。stream の既定値補完に渡す。</summary>
    public static YamlMappingNode ParseTemplateText(string text) => ParseText(text);

    private static YamlMappingNode ParseText(string text)
    {
        var stream = new YamlStream();
        using var reader = new StringReader(text);
        stream.Load(reader);
        return stream.Documents.Count == 0
            ? []
            : stream.Documents[0].RootNode as YamlMappingNode ?? [];
    }

    private static EpgStationConfigFile FormatConfig(YamlMappingNode config, string rootPath, YamlMappingNode? template)
    {
        // --- http / https のどちらかは要る (formatConfig の最初の検査) ---
        YamlMappingNode? https = Map(config, "https");
        bool hasPort = Has(config, "port");
        bool httpsUsable = https is not null && Has(https, "port") && Has(https, "key") && Has(https, "cert");
        if (!hasPort && !httpsUsable)
        {
            throw new PortSettingException();
        }

        int? port = Int(config, "port");

        // --- apiServers: 空なら http://localhost:<port> を 1 件入れる ---
        List<string> apiServers = StringList(config, "apiServers") ?? [];
        if (apiServers.Count == 0)
        {
            // port 未設定 (https のみ) の場合、上流は文字列 "undefined" を埋め込む。同じ値を出す。
            apiServers.Add($"http://localhost:{(port.HasValue ? port.Value.ToString(CultureInfo.InvariantCulture) : "undefined")}");
        }

        // --- subDirectory: urljoin('/', x) してから末尾の '/' を 1 つ落とす ---
        string? subDirectory = String(config, "subDirectory");
        if (subDirectory is not null)
        {
            subDirectory = UrlJoin.Join("/", subDirectory);
            if (subDirectory.EndsWith('/'))
            {
                subDirectory = subDirectory[..^1];
            }
        }

        // --- recorded: パス整形のあと name == "tmp" を除く ---
        List<EpgStationRecordedDirInfo> recorded = ReadRecorded(config, rootPath);
        recorded = [.. recorded.Where(directory => !string.Equals(directory.Name, "tmp", StringComparison.Ordinal))];

        string? recordedTmp = String(config, "recordedTmp");
        if (recordedTmp is not null)
        {
            recordedTmp = DirectoryFormatting(recordedTmp, rootPath);
        }

        return new EpgStationConfigFile
        {
            Port = port,
            SocketioPort = Int(config, "socketioPort"),
            ClientSocketioPort = Int(config, "clientSocketioPort"),
            Https = ReadHttps(https),
            MirakurunPath = String(config, "mirakurunPath") ?? DefaultMirakurunPath,
            SubDirectory = subDirectory,
            Uid = String(config, "uid"),
            Gid = String(config, "gid"),
            ApiServers = apiServers,
            IsAllowAllCors = Bool(config, "isAllowAllCORS") ?? false,
            DbType = String(config, "dbtype") ?? "sqlite",
            Sqlite = ReadSqlite(Map(config, "sqlite")),
            MySql = ReadMySql(Map(config, "mysql")),
            Postgres = ReadPostgres(Map(config, "postgres")),
            NeedToReplaceEnclosingCharacters = Bool(config, "needToReplaceEnclosingCharacters") ?? true,
            EpgUpdateIntervalTime = Int(config, "epgUpdateIntervalTime") ?? 10,
            ChannelOrder = LongList(config, "channelOrder"),
            SidOrder = IntList(config, "sidOrder"),
            ExcludeChannels = LongList(config, "excludeChannels"),
            ExcludeSids = IntList(config, "excludeSids"),
            RecPriority = Int(config, "recPriority") ?? 2,
            ConflictPriority = Int(config, "conflictPriority") ?? 1,
            StreamingPriority = Int(config, "streamingPriority") ?? 0,
            TimeSpecifiedStartMargin = Int(config, "timeSpecifiedStartMargin") ?? 1,
            TimeSpecifiedEndMargin = Int(config, "timeSpecifiedEndMargin") ?? 1,
            RecordedFormat = String(config, "recordedFormat") ?? DefaultRecordedFormat,
            RecordedFileExtension = String(config, "recordedFileExtension") ?? ".ts",
            Recorded = recorded,
            RecordedTmp = recordedTmp,
            RecordedHistoryRetentionPeriodDays = Int(config, "recordedHistoryRetentionPeriodDays") ?? 90,
            StorageLimitCheckIntervalTime = Int(config, "storageLimitCheckIntervalTime") ?? 60,
            Thumbnail = DirectoryFormatting(String(config, "thumbnail") ?? Combine(rootPath, "thumbnail"), rootPath),
            ThumbnailCmd = String(config, "thumbnailCmd") ?? DefaultThumbnailCmd,
            ThumbnailSize = String(config, "thumbnailSize") ?? "480x270",
            ThumbnailPosition = Int(config, "thumbnailPosition") ?? 5,
            DropLog = String(config, "dropLog") ?? Combine(rootPath, "drop"),
            IsEnabledDropCheck = Bool(config, "isEnabledDropCheck") ?? false,
            UploadTempDir = String(config, "uploadTempDir") ?? Combine(rootPath, "data", "upload"),
            Ffmpeg = String(config, "ffmpeg") ?? "/usr/local/bin/ffmpeg",
            Ffprobe = String(config, "ffprobe") ?? "/usr/local/bin/ffprobe",
            EncodeProcessNum = Int(config, "encodeProcessNum") ?? 0,
            ConcurrentEncodeNum = Int(config, "concurrentEncodeNum") ?? 0,
            Encode = ReadEncode(config),
            IsSuppressReservesUpdateAllLog = Bool(config, "isSuppressReservesUpdateAllLog") ?? false,
            ReserveNewAddtionCommand = String(config, "reserveNewAddtionCommand"),
            ReserveUpdateCommand = String(config, "reserveUpdateCommand"),
            ReservedeletedCommand = String(config, "reservedeletedCommand"),
            RecordingPreStartCommand = String(config, "recordingPreStartCommand"),
            RecordingPrepRecFailedCommand = String(config, "recordingPrepRecFailedCommand"),
            RecordingStartCommand = String(config, "recordingStartCommand"),
            RecordingFinishCommand = String(config, "recordingFinishCommand"),
            RecordingFailedCommand = String(config, "recordingFailedCommand"),
            EncodingFinishCommand = String(config, "encodingFinishCommand"),
            UrlScheme = Has(config, "urlscheme") ? ReadUrlScheme(Map(config, "urlscheme")) : DefaultUrlScheme,
            StreamFilePath = DirectoryFormatting(
                String(config, "streamFilePath") ?? Combine(rootPath, "data", "streamfiles"),
                rootPath),
            Stream = ReadStream(Map(config, "stream"), template),
            KodiHosts = ReadKodiHosts(config),
        };
    }


    internal const string DefaultMirakurunPath = "http+unix://%2Fvar%2Frun%2Fmirakurun.sock/";

    internal const string DefaultRecordedFormat = "%YEAR%年%MONTH%月%DAY%日%HOUR%時%MIN%分%SEC%秒-%TITLE%";

    internal const string DefaultThumbnailCmd =
        "%FFMPEG% -ss %THUMBNAIL_POSITION% -y -i %INPUT% -vframes 1 -f image2 -s %THUMBNAIL_SIZE% %OUTPUT%";

    internal static EpgStationUrlSchemeConfig DefaultUrlScheme { get; } = new()
    {
        M2Ts = new EpgStationUrlSchemeInfo
        {
            Ios = "vlc-x-callback://x-callback-url/stream?url=PROTOCOL%3A%2F%2FADDRESS\"",
            Android = "intent://ADDRESS#Intent;action=android.intent.action.VIEW;type=video/*;scheme=PROTOCOL;end",
        },
        Video = new EpgStationUrlSchemeInfo
        {
            Ios = "infuse://x-callback-url/play?url=PROTOCOL://ADDRESS",
            Android = "intent://ADDRESS#Intent;action=android.intent.action.VIEW;type=video/*;scheme=PROTOCOL;end",
        },
        Download = new EpgStationUrlSchemeInfo
        {
            Ios = "vlc-x-callback://x-callback-url/stream?url=PROTOCOL%3A%2F%2FADDRESS&filename=FILENAME",
        },
    };


    private static List<EpgStationRecordedDirInfo> ReadRecorded(YamlMappingNode config, string rootPath)
    {
        YamlSequenceNode? sequence = Sequence(config, "recorded");
        if (sequence is null)
        {
            return
            [
                new EpgStationRecordedDirInfo { Name = "recorded", Path = Combine(rootPath, "recorded") },
            ];
        }

        List<EpgStationRecordedDirInfo> result = [];
        foreach (YamlNode node in sequence)
        {
            if (node is not YamlMappingNode entry)
            {
                continue;
            }

            result.Add(new EpgStationRecordedDirInfo
            {
                Name = String(entry, "name") ?? string.Empty,
                Path = DirectoryFormatting(String(entry, "path") ?? string.Empty, rootPath),
                LimitThreshold = Long(entry, "limitThreshold"),
                Action = String(entry, "action"),
                LimitCmd = String(entry, "limitCmd"),
            });
        }

        return result;
    }

    private static List<EpgStationEncodeInfo> ReadEncode(YamlMappingNode config)
    {
        YamlSequenceNode? sequence = Sequence(config, "encode");
        if (sequence is null)
        {
            return [];
        }

        List<EpgStationEncodeInfo> result = [];
        foreach (YamlNode node in sequence)
        {
            if (node is not YamlMappingNode entry)
            {
                continue;
            }

            result.Add(new EpgStationEncodeInfo
            {
                Name = String(entry, "name") ?? string.Empty,
                Cmd = String(entry, "cmd") ?? string.Empty,
                Suffix = String(entry, "suffix"),
                Rate = Double(entry, "rate"),
            });
        }

        return result;
    }

    private static EpgStationHttpsConfig? ReadHttps(YamlMappingNode? https) => https is null
        ? null
        : new EpgStationHttpsConfig
        {
            Port = Int(https, "port"),
            Key = String(https, "key"),
            Cert = String(https, "cert"),
            Ca = StringOrStringList(https, "ca"),
            SocketioPort = Int(https, "socketioPort"),
        };

    private static EpgStationSqliteConfig? ReadSqlite(YamlMappingNode? node) => node is null
        ? null
        : new EpgStationSqliteConfig
        {
            Extensions = StringList(node, "extensions"),
            Regexp = Bool(node, "regexp"),
        };

    private static EpgStationMySqlConfig? ReadMySql(YamlMappingNode? node) => node is null
        ? null
        : new EpgStationMySqlConfig
        {
            Host = String(node, "host") ?? string.Empty,
            User = String(node, "user") ?? string.Empty,
            Port = Int(node, "port") ?? 3306,
            Password = String(node, "password") ?? string.Empty,
            Database = String(node, "database") ?? string.Empty,
            Charset = String(node, "charset"),
        };

    private static EpgStationPostgresConfig? ReadPostgres(YamlMappingNode? node) => node is null
        ? null
        : new EpgStationPostgresConfig
        {
            Host = String(node, "host") ?? string.Empty,
            User = String(node, "user") ?? string.Empty,
            Port = Int(node, "port") ?? 5432,
            Database = String(node, "database") ?? string.Empty,
            Password = String(node, "password") ?? string.Empty,
        };

    private static EpgStationUrlSchemeConfig? ReadUrlScheme(YamlMappingNode? node) => node is null
        ? null
        : new EpgStationUrlSchemeConfig
        {
            M2Ts = ReadUrlSchemeInfo(Map(node, "m2ts")),
            Video = ReadUrlSchemeInfo(Map(node, "video")),
            Download = ReadUrlSchemeInfo(Map(node, "download")),
        };

    private static EpgStationUrlSchemeInfo? ReadUrlSchemeInfo(YamlMappingNode? node) => node is null
        ? null
        : new EpgStationUrlSchemeInfo
        {
            Ios = String(node, "ios"),
            Android = String(node, "android"),
            Mac = String(node, "mac"),
            Win = String(node, "win"),
        };

    private static List<EpgStationKodiInfo>? ReadKodiHosts(YamlMappingNode config)
    {
        YamlSequenceNode? sequence = Sequence(config, "kodiHosts");
        if (sequence is null)
        {
            return null;
        }

        List<EpgStationKodiInfo> result = [];
        foreach (YamlNode node in sequence)
        {
            if (node is not YamlMappingNode entry)
            {
                continue;
            }

            result.Add(new EpgStationKodiInfo
            {
                Name = String(entry, "name") ?? string.Empty,
                Host = String(entry, "host") ?? string.Empty,
                User = String(entry, "user"),
                Password = String(entry, "password"),
            });
        }

        return result;
    }

    /// <summary>
    /// <c>setTemplateValues</c> の stream 部分。config.yml が <c>stream.live.ts</c> や
    /// <c>stream.recorded.ts</c> を持つのに、その下の形式を書いていないときだけ template で埋める。
    /// template が読めなければ (上流の <c>templateConfig === null</c>) 何もしない。
    /// </summary>
    private static EpgStationStreamConfig? ReadStream(YamlMappingNode? stream, YamlMappingNode? template)
    {
        if (stream is null)
        {
            return null;
        }

        YamlMappingNode? templateStream = template is null ? null : Map(template, "stream");
        YamlMappingNode? live = Map(stream, "live");
        YamlMappingNode? recorded = Map(stream, "recorded");

        EpgStationLiveTsConfig? liveTs = null;
        if (live is not null && Map(live, "ts") is { } liveTsNode)
        {
            YamlMappingNode? templateLiveTs = templateStream is null
                ? null
                : Map(Map(templateStream, "live"), "ts");
            liveTs = new EpgStationLiveTsConfig
            {
                M2Ts = Cmds(liveTsNode, "m2ts") ?? Cmds(templateLiveTs, "m2ts"),
                M2TsLl = Cmds(liveTsNode, "m2tsll") ?? Cmds(templateLiveTs, "m2tsll"),
                Webm = Cmds(liveTsNode, "webm") ?? Cmds(templateLiveTs, "webm"),
                Mp4 = Cmds(liveTsNode, "mp4") ?? Cmds(templateLiveTs, "mp4"),
                Hls = Cmds(liveTsNode, "hls") ?? Cmds(templateLiveTs, "hls"),
            };
        }

        EpgStationRecordedTsConfig? recordedTs = null;
        EpgStationRecordedTsConfig? recordedEncoded = null;
        if (recorded is not null)
        {
            YamlMappingNode? templateRecorded = templateStream is null ? null : Map(templateStream, "recorded");
            if (Map(recorded, "ts") is { } recordedTsNode)
            {
                YamlMappingNode? templateRecordedTs = Map(templateRecorded, "ts");
                recordedTs = new EpgStationRecordedTsConfig
                {
                    Webm = Cmds(recordedTsNode, "webm") ?? Cmds(templateRecordedTs, "webm"),
                    Mp4 = Cmds(recordedTsNode, "mp4") ?? Cmds(templateRecordedTs, "mp4"),
                    Hls = Cmds(recordedTsNode, "hls") ?? Cmds(templateRecordedTs, "hls"),
                };
            }

            if (Map(recorded, "encoded") is { } recordedEncodedNode)
            {
                YamlMappingNode? templateRecordedEncoded = Map(templateRecorded, "encoded");
                recordedEncoded = new EpgStationRecordedTsConfig
                {
                    Webm = Cmds(recordedEncodedNode, "webm") ?? Cmds(templateRecordedEncoded, "webm"),
                    Mp4 = Cmds(recordedEncodedNode, "mp4") ?? Cmds(templateRecordedEncoded, "mp4"),
                    Hls = Cmds(recordedEncodedNode, "hls") ?? Cmds(templateRecordedEncoded, "hls"),
                };
            }
        }

        return new EpgStationStreamConfig
        {
            Live = live is null ? null : new EpgStationLiveStreamConfig { Ts = liveTs },
            Recorded = recorded is null
                ? null
                : new EpgStationRecordedStreamConfig { Ts = recordedTs, Encoded = recordedEncoded },
        };
    }

    private static List<EpgStationStreamingCmd>? Cmds(YamlMappingNode? node, string key)
    {
        YamlSequenceNode? sequence = node is null ? null : Sequence(node, key);
        if (sequence is null)
        {
            return null;
        }

        List<EpgStationStreamingCmd> result = [];
        foreach (YamlNode item in sequence)
        {
            if (item is not YamlMappingNode entry)
            {
                continue;
            }

            result.Add(new EpgStationStreamingCmd
            {
                Name = String(entry, "name") ?? string.Empty,
                Cmd = String(entry, "cmd"),
            });
        }

        return result;
    }


    /// <summary>
    /// <c>Configuration.directoryFormatting</c>。<c>%ROOT%</c> を 1 度だけ置き換え、末尾の
    /// パス区切り文字を 1 つ落とす。上流の <c>String.prototype.replace</c> は最初の 1 件だけを
    /// 置き換えるので、こちらも 1 件だけにしてある。
    /// </summary>
    internal static string DirectoryFormatting(string directory, string rootPath)
    {
        int index = directory.IndexOf("%ROOT%", StringComparison.Ordinal);
        string replaced = index < 0
            ? directory
            : string.Concat(directory.AsSpan(0, index), rootPath, directory.AsSpan(index + "%ROOT%".Length));

        return replaced.Length > 0 && replaced[^1] == Path.DirectorySeparatorChar
            ? replaced[..^1]
            : replaced;
    }

    private static string Combine(string root, params string[] parts) =>
        Path.Combine([root, .. parts]);


    private static bool Has(YamlMappingNode node, string key) =>
        node.Children.TryGetValue(new YamlScalarNode(key), out YamlNode? value) && !IsNull(value);

    private static YamlNode? Get(YamlMappingNode? node, string key)
    {
        if (node is null || !node.Children.TryGetValue(new YamlScalarNode(key), out YamlNode? value))
        {
            return null;
        }

        return IsNull(value) ? null : value;
    }

    /// <summary>
    /// js-yaml の core schema と同じ null 判定。plain scalar の <c>~</c>、<c>null</c>、<c>Null</c>、
    /// <c>NULL</c>、空だけを null とし、引用符付きの空文字列は文字列として扱う。
    /// </summary>
    private static bool IsNull(YamlNode node) =>
        node is YamlScalarNode { Style: YamlDotNet.Core.ScalarStyle.Any or YamlDotNet.Core.ScalarStyle.Plain } scalar &&
        scalar.Value is null or "" or "~" or "null" or "Null" or "NULL";

    private static YamlMappingNode? Map(YamlMappingNode? node, string key) => Get(node, key) as YamlMappingNode;

    private static YamlSequenceNode? Sequence(YamlMappingNode node, string key) => Get(node, key) as YamlSequenceNode;

    private static string? String(YamlMappingNode node, string key) => (Get(node, key) as YamlScalarNode)?.Value;

    private static bool? Bool(YamlMappingNode node, string key) => (Get(node, key) as YamlScalarNode)?.Value switch
    {
        "true" or "True" or "TRUE" => true,
        "false" or "False" or "FALSE" => false,
        _ => null,
    };

    private static int? Int(YamlMappingNode node, string key) =>
        int.TryParse((Get(node, key) as YamlScalarNode)?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
            ? value
            : null;

    private static long? Long(YamlMappingNode node, string key) =>
        long.TryParse((Get(node, key) as YamlScalarNode)?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long value)
            ? value
            : null;

    private static double? Double(YamlMappingNode node, string key) =>
        double.TryParse((Get(node, key) as YamlScalarNode)?.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
            ? value
            : null;

    private static List<string>? StringList(YamlMappingNode node, string key)
    {
        if (Get(node, key) is not YamlSequenceNode sequence)
        {
            return null;
        }

        return [.. sequence.OfType<YamlScalarNode>().Select(item => item.Value ?? string.Empty)];
    }

    /// <summary>https.ca は文字列 1 つでも配列でも書ける。</summary>
    private static List<string>? StringOrStringList(YamlMappingNode node, string key) => Get(node, key) switch
    {
        YamlScalarNode { Value: { } value } => [value],
        YamlSequenceNode sequence => [.. sequence.OfType<YamlScalarNode>().Select(item => item.Value ?? string.Empty)],
        _ => null,
    };

    private static List<long>? LongList(YamlMappingNode node, string key)
    {
        if (Get(node, key) is not YamlSequenceNode sequence)
        {
            return null;
        }

        return
        [
            .. sequence.OfType<YamlScalarNode>()
                .Select(item => long.TryParse(item.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long value)
                    ? value
                    : (long?)null)
                .Where(value => value.HasValue)
                .Select(value => value!.Value),
        ];
    }

    private static List<int>? IntList(YamlMappingNode node, string key)
    {
        if (Get(node, key) is not YamlSequenceNode sequence)
        {
            return null;
        }

        return
        [
            .. sequence.OfType<YamlScalarNode>()
                .Select(item => int.TryParse(item.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
                    ? value
                    : (int?)null)
                .Where(value => value.HasValue)
                .Select(value => value!.Value),
        ];
    }
}
