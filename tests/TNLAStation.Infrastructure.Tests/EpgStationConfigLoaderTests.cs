using Microsoft.Extensions.Configuration;
using TNLAStation.Infrastructure.Configuration;
using TNLAStation.Infrastructure.Configuration.EpgStation;
using TNLAStation.Infrastructure.Mirakurun;

namespace TNLAStation.Infrastructure.Tests;

/// <summary>
/// EPGStation の config.yml をそのまま読めることの契約試験。
///
/// 根拠は EPGStation v2.10.0 (5cf2ea383d37937eacecf424820dbd7a278d577e) の
/// <c>src/model/Configuration.ts</c>。既定値・%ROOT% 展開・末尾スラッシュ・tmp 除外・
/// テンプレート補完・subDirectory の整形が、どれも外から見える形で効くので個別に固定する。
/// </summary>
public sealed class EpgStationConfigLoaderTests
{
    private const string Root = "/opt/epgstation";

    private static EpgStationConfigFile Load(string yaml, string? template = null) =>
        EpgStationConfigLoader.Parse(
            yaml,
            Root,
            template is null ? null : EpgStationConfigLoader.ParseTemplateText(template));


    [Fact]
    public void AConfigWithOnlyAPortGetsEveryDefaultFromUpstream()
    {
        EpgStationConfigFile config = Load("port: 8888\n");

        Assert.Equal(8888, config.Port);
        Assert.Equal("http+unix://%2Fvar%2Frun%2Fmirakurun.sock/", config.MirakurunPath);
        Assert.Equal(["http://localhost:8888"], config.ApiServers);
        Assert.False(config.IsAllowAllCors);
        Assert.Equal("sqlite", config.DbType);
        Assert.True(config.NeedToReplaceEnclosingCharacters);
        Assert.Equal(10, config.EpgUpdateIntervalTime);
        Assert.Equal(1, config.ConflictPriority);
        Assert.Equal(2, config.RecPriority);
        Assert.Equal(0, config.StreamingPriority);
        Assert.Equal(1, config.TimeSpecifiedStartMargin);
        Assert.Equal(1, config.TimeSpecifiedEndMargin);
        Assert.Equal("%YEAR%年%MONTH%月%DAY%日%HOUR%時%MIN%分%SEC%秒-%TITLE%", config.RecordedFormat);
        Assert.Equal(".ts", config.RecordedFileExtension);
        Assert.Equal(90, config.RecordedHistoryRetentionPeriodDays);
        Assert.Equal(60, config.StorageLimitCheckIntervalTime);
        Assert.Equal("480x270", config.ThumbnailSize);
        Assert.Equal(5, config.ThumbnailPosition);
        Assert.False(config.IsEnabledDropCheck);
        Assert.Equal("/usr/local/bin/ffmpeg", config.Ffmpeg);
        Assert.Equal("/usr/local/bin/ffprobe", config.Ffprobe);
        Assert.Equal(0, config.EncodeProcessNum);
        Assert.Equal(0, config.ConcurrentEncodeNum);
        Assert.Empty(config.Encode);
        Assert.False(config.IsSuppressReservesUpdateAllLog);
        Assert.Null(config.Stream);
        Assert.Null(config.KodiHosts);

        // パスの既定値は %ROOT% 相当の場所に置かれる。
        Assert.Equal(Path.Combine(Root, "recorded"), Assert.Single(config.Recorded).Path);
        Assert.Equal("recorded", config.Recorded[0].Name);
        Assert.Equal(Path.Combine(Root, "thumbnail"), config.Thumbnail);
        Assert.Equal(Path.Combine(Root, "drop"), config.DropLog);
        Assert.Equal(Path.Combine(Root, "data", "upload"), config.UploadTempDir);
        Assert.Equal(Path.Combine(Root, "data", "streamfiles"), config.StreamFilePath);
        Assert.Equal(
            "%FFMPEG% -ss %THUMBNAIL_POSITION% -y -i %INPUT% -vframes 1 -f image2 -s %THUMBNAIL_SIZE% %OUTPUT%",
            config.ThumbnailCmd);
    }

    [Fact]
    public void TheDefaultUrlSchemeMatchesUpstreamIncludingItsStrayQuote()
    {
        EpgStationConfigFile config = Load("port: 8888\n");

        // 上流の DEFAULT_VALUE には m2ts.ios の末尾に " が入っている。直すと値が変わるので写す。
        Assert.Equal(
            "vlc-x-callback://x-callback-url/stream?url=PROTOCOL%3A%2F%2FADDRESS\"",
            config.UrlScheme!.M2Ts!.Ios);
        Assert.Equal("infuse://x-callback-url/play?url=PROTOCOL://ADDRESS", config.UrlScheme.Video!.Ios);
        Assert.Equal(
            "vlc-x-callback://x-callback-url/stream?url=PROTOCOL%3A%2F%2FADDRESS&filename=FILENAME",
            config.UrlScheme.Download!.Ios);
        Assert.Null(config.UrlScheme.Download.Android);
    }

    [Fact]
    public void WithoutAPortAndWithoutACompleteHttpsBlockTheConfigIsRejected()
    {
        Assert.Throws<EpgStationConfigLoader.PortSettingException>(() => Load("mirakurunPath: http://localhost:40772\n"));
        Assert.Throws<EpgStationConfigLoader.PortSettingException>(() => Load("https:\n    port: 8443\n"));
    }

    [Fact]
    public void AnHttpsOnlyConfigIsAccepted()
    {
        EpgStationConfigFile config = Load("""
            https:
                port: 8443
                key: /etc/ssl/key.pem
                cert: /etc/ssl/cert.pem
            """);

        Assert.Null(config.Port);
        Assert.Equal(8443, config.Https!.Port);
        // port が無いので、上流は文字列化した undefined をそのまま埋め込む。
        Assert.Equal(["http://localhost:undefined"], config.ApiServers);
    }


    [Theory]
    [InlineData("%ROOT%/recorded", "/opt/epgstation/recorded")]
    [InlineData("%ROOT%/recorded/", "/opt/epgstation/recorded")]
    [InlineData("/mnt/rec/", "/mnt/rec")]
    [InlineData("/mnt/rec", "/mnt/rec")]
    [InlineData("/mnt/rec//", "/mnt/rec/")]
    public void RecordedPathsExpandRootAndLoseExactlyOneTrailingSeparator(string input, string expected)
    {
        EpgStationConfigFile config = Load($"""
            port: 8888
            recorded:
                - name: recorded
                  path: '{input}'
            """);

        Assert.Equal(expected, Assert.Single(config.Recorded).Path);
    }

    [Fact]
    public void ARecordedEntryNamedTmpIsDropped()
    {
        EpgStationConfigFile config = Load("""
            port: 8888
            recorded:
                - name: recorded
                  path: /mnt/rec
                - name: tmp
                  path: /mnt/tmp
                - name: anime
                  path: /mnt/anime
            """);

        Assert.Equal(["recorded", "anime"], config.Recorded.Select(directory => directory.Name));
    }

    [Fact]
    public void ThumbnailStreamFilePathAndRecordedTmpAreFormattedToo()
    {
        EpgStationConfigFile config = Load("""
            port: 8888
            thumbnail: '%ROOT%/thumbnail/'
            streamFilePath: '%ROOT%/data/streamfiles/'
            recordedTmp: '%ROOT%/tmp/'
            dropLog: '%ROOT%/drop/'
            """);

        Assert.Equal("/opt/epgstation/thumbnail", config.Thumbnail);
        Assert.Equal("/opt/epgstation/data/streamfiles", config.StreamFilePath);
        Assert.Equal("/opt/epgstation/tmp", config.RecordedTmp);
        // dropLog は directoryFormatting を通らない。%ROOT% も末尾スラッシュもそのまま残る。
        Assert.Equal("%ROOT%/drop/", config.DropLog);
    }


    [Theory]
    [InlineData("tnla", "/tnla")]
    [InlineData("/tnla", "/tnla")]
    [InlineData("/tnla/", "/tnla")]
    [InlineData("tnla/sub", "/tnla/sub")]
    [InlineData("", "")]
    public void SubDirectoryIsNormalisedTheWayUrlJoinDoes(string input, string expected)
    {
        EpgStationConfigFile config = Load($"port: 8888\nsubDirectory: '{input}'\n");

        Assert.Equal(expected, config.SubDirectory);
    }

    [Fact]
    public void WithoutSubDirectoryTheValueStaysAbsent()
    {
        Assert.Null(Load("port: 8888\n").SubDirectory);
    }


    [Fact]
    public void StreamSubKeysAreFilledFromTheTemplateOnlyWhenTheParentIsPresent()
    {
        const string template = """
            stream:
                live:
                    ts:
                        m2ts:
                            - name: template-m2ts
                        hls:
                            - name: template-hls
                recorded:
                    ts:
                        mp4:
                            - name: template-recorded-mp4
            """;

        EpgStationConfigFile config = Load(
            """
            port: 8888
            stream:
                live:
                    ts:
                        m2ts:
                            - name: 720p
                              cmd: 'ffmpeg'
                recorded:
                    ts: {}
            """,
            template);

        // 自分で書いた m2ts はそのまま。書かなかった hls は template から入る。
        Assert.Equal(["720p"], config.Stream!.Live!.Ts!.M2Ts!.Select(cmd => cmd.Name));
        Assert.Equal(["template-hls"], config.Stream.Live.Ts.Hls!.Select(cmd => cmd.Name));
        // template にも無い形式は入らない。
        Assert.Null(config.Stream.Live.Ts.Webm);
        Assert.Equal(["template-recorded-mp4"], config.Stream.Recorded!.Ts!.Mp4!.Select(cmd => cmd.Name));
        // stream.recorded.encoded を書いていないので encoded は生えない。
        Assert.Null(config.Stream.Recorded.Encoded);
    }

    [Fact]
    public void WithoutATemplateNoStreamDefaultsAreFilledIn()
    {
        EpgStationConfigFile config = Load("""
            port: 8888
            stream:
                live:
                    ts:
                        m2ts:
                            - name: 無変換
            """);

        Assert.Equal(["無変換"], config.Stream!.Live!.Ts!.M2Ts!.Select(cmd => cmd.Name));
        Assert.Null(config.Stream.Live.Ts.Hls);
        Assert.Null(config.Stream.Recorded);
    }

    [Fact]
    public void AStreamingCmdWithoutACmdIsTheUnconvertedOne()
    {
        EpgStationConfigFile config = Load("""
            port: 8888
            stream:
                live:
                    ts:
                        m2ts:
                            - name: 720p
                              cmd: '%FFMPEG% -i pipe:0 pipe:1'
                            - name: 無変換
            """);

        Assert.Equal("%FFMPEG% -i pipe:0 pipe:1", config.Stream!.Live!.Ts!.M2Ts![0].Cmd);
        Assert.Null(config.Stream.Live.Ts.M2Ts[1].Cmd);
    }


    [Fact]
    public void TheShippedEpgStationTemplateLoadsAndKeepsItsProfileOrder()
    {
        string templatePath = FindConfigTemplate();
        EpgStationConfigFile config = EpgStationConfigLoader.Load(templatePath, Root);

        Assert.Equal(8888, config.Port);
        Assert.Equal(".m2ts", config.RecordedFileExtension);
        Assert.Equal(4, config.EncodeProcessNum);
        Assert.Equal(1, config.ConcurrentEncodeNum);
        Assert.Equal(["H.264"], config.Encode.Select(mode => mode.Name));
        Assert.Equal(".mp4", config.Encode[0].Suffix);
        Assert.Equal(4.0, config.Encode[0].Rate);

        // ライブ m2ts は 720p / 480p / 無変換 の順。順序は画面の選択肢の並びとして外から見える。
        Assert.Equal(["720p", "480p", "無変換"], config.Stream!.Live!.Ts!.M2Ts!.Select(cmd => cmd.Name));
        Assert.Null(config.Stream.Live.Ts.M2Ts![2].Cmd);
        Assert.Equal(["720p", "480p"], config.Stream.Live.Ts.M2TsLl!.Select(cmd => cmd.Name));
        Assert.Equal(["720p", "480p"], config.Stream.Recorded!.Encoded!.Hls!.Select(cmd => cmd.Name));
    }


    [Fact]
    public void TheConfigFileFeedsTheSameOptionsTheRestOfTheCodeAlreadyUses()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"tnla-config-{Guid.NewGuid():N}", "config");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "config.yml");
        File.WriteAllText(path, """
            port: 8888
            socketioPort: 8889
            clientSocketioPort: 8890
            subDirectory: /tnla
            isAllowAllCORS: true
            mirakurunPath: http+unix://%2Fvar%2Frun%2Fmirakurun.sock/
            epgUpdateIntervalTime: 20
            recPriority: 5
            conflictPriority: 4
            streamingPriority: 3
            timeSpecifiedStartMargin: 7
            timeSpecifiedEndMargin: 9
            recordedFileExtension: .m2ts
            recorded:
                - name: recorded
                  path: '%ROOT%/recorded'
                  limitThreshold: 20000
                  action: remove
            recordedHistoryRetentionPeriodDays: 30
            storageLimitCheckIntervalTime: 120
            thumbnailSize: 640x360
            thumbnailPosition: 12
            isEnabledDropCheck: true
            uploadTempDir: '%ROOT%/data/upload'
            concurrentEncodeNum: 2
            encodeProcessNum: 6
            encode:
                - name: H.264
                  cmd: '%NODE% %ROOT%/config/enc.js'
                  suffix: .mp4
                  rate: 3.5
            isSuppressReservesUpdateAllLog: true
            recordingStartCommand: /bin/sh %ROOT%/config/start.sh
            kodiHosts:
                - name: living
                  host: http://192.168.1.10:8080/some/path
                  user: kodi
                  password: secret
            """);

        try
        {
            IConfigurationRoot configuration = new ConfigurationBuilder()
                .AddEpgStationConfigFile(path, reloadOnChange: false)
                .Build();

            Assert.Equal("/tnla", configuration.GetSection("Api").Get<ApiOptions>()!.SubDirectory);
            Assert.True(configuration.GetSection("Api").Get<ApiOptions>()!.IsAllowAllCors);
            Assert.Equal(["http://localhost:8888"], configuration.GetSection("Api").Get<ApiOptions>()!.Servers);

            ServerOptions server = configuration.GetSection("Server").Get<ServerOptions>()!;
            Assert.Equal(8888, server.Port);
            Assert.Equal(8889, server.SocketIoPort);
            Assert.Equal(8890, server.ClientSocketIoPort);

            MirakurunOptions mirakurun = configuration.GetSection("Mirakurun").Get<MirakurunOptions>()!;
            Assert.Equal("http+unix://%2Fvar%2Frun%2Fmirakurun.sock/", mirakurun.BaseUrl);
            Assert.Equal("/usr/local/bin/ffmpeg", configuration["Ffmpeg:FfmpegPath"]);
            Assert.Equal("/usr/local/bin/ffprobe", configuration["Ffmpeg:FfprobePath"]);
            Assert.Equal("6", configuration["Ffmpeg:EncodeProcessNum"]);
            Assert.Equal(5, mirakurun.RecPriority);
            Assert.Equal(4, mirakurun.ConflictPriority);
            Assert.Equal(3, mirakurun.StreamingPriority);

            EpgOptions epg = configuration.GetSection("Epg").Get<EpgOptions>()!;
            Assert.Equal(20, epg.UpdateIntervalMinutes);

            RecordingOptions recording = configuration.GetSection("Recording").Get<RecordingOptions>()!;
            Assert.Equal(7, recording.StartMarginSeconds);
            Assert.Equal(9, recording.EndMarginSeconds);
            Assert.Equal(".m2ts", recording.RecordedFileExtension);
            Assert.True(recording.IsEnabledDropCheck);

            ReserveOptions reserve = configuration.GetSection("Reserve").Get<ReserveOptions>()!;
            Assert.Equal(30, reserve.RecordedHistoryRetentionPeriodDays);
            Assert.True(reserve.IsSuppressReservesUpdateAllLog);

            StorageOptions storage = configuration.GetSection("Storage").Get<StorageOptions>()!;
            RecordedDirectoryOptions recordedDirectory = Assert.Single(storage.RecordedDirectories);
            Assert.Equal("recorded", recordedDirectory.Name);
            Assert.Equal(Path.Combine(Path.GetDirectoryName(directory)!, "recorded"), recordedDirectory.Path);
            Assert.Equal(20000, recordedDirectory.LimitThresholdMb);
            Assert.Equal("remove", recordedDirectory.Action);
            Assert.Equal(120, storage.StorageLimitCheckIntervalSeconds);

            ThumbnailOptions thumbnail = configuration.GetSection("Thumbnail").Get<ThumbnailOptions>()!;
            Assert.Equal(640, thumbnail.Width);
            Assert.Equal(360, thumbnail.Height);
            Assert.Equal(12, thumbnail.PositionSeconds);

            EncodeOptions encode = configuration.GetSection("Encode").Get<EncodeOptions>()!;
            Assert.Equal(2, encode.ConcurrentEncodeNum);
            Assert.Equal(6, encode.ProcessNum);
            EncodeModeOptions mode = Assert.Single(encode.Modes);
            Assert.Equal("H.264", mode.Name);
            Assert.Equal(".mp4", mode.Extension);
            Assert.Equal(3.5, mode.RateTimeoutMultiplier);

            CommandHookOptions hooks = configuration.GetSection("CommandHooks").Get<CommandHookOptions>()!;
            Assert.Equal(
                $"/bin/sh {Path.GetDirectoryName(directory)!}/config/start.sh",
                hooks.RecordingStartCommand);
            Assert.Null(hooks.RecordingFinishCommand);

            KodiOptions kodi = configuration.GetSection("Kodi").Get<KodiOptions>()!;
            KodiHostOptions host = Assert.Single(kodi.Hosts);
            Assert.Equal("living", host.Name);
            // 上流は url.resolve(host, '/jsonrpc')。host のパスは捨てられる。
            Assert.Equal("http://192.168.1.10:8080/jsonrpc", host.Url);
            Assert.Equal("kodi", host.User);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(directory)!, recursive: true);
        }
    }

    [Fact]
    public void RewritingConfigYmlIsPickedUpWithoutARestart()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"tnla-config-{Guid.NewGuid():N}", "config");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "config.yml");
        File.WriteAllText(path, "port: 8888\nepgUpdateIntervalTime: 10\n");

        try
        {
            var source = new EpgStationConfigurationSource
            {
                ConfigPath = path,
                RootPath = Path.GetDirectoryName(directory)!,
                Optional = false,
                ReloadOnChange = false,
            };
            var provider = new EpgStationConfigurationProvider(source);
            provider.Load();
            Assert.True(provider.TryGet("Epg:UpdateIntervalMinutes", out string? before));
            Assert.Equal("10", before);

            File.WriteAllText(path, "port: 8888\nepgUpdateIntervalTime: 45\n");
            provider.PollForChanges();

            Assert.True(provider.TryGet("Epg:UpdateIntervalMinutes", out string? after));
            Assert.Equal("45", after);

            // 壊れた config を書いても、直前に読めた設定のまま動き続ける。
            File.WriteAllText(path, "port: 8888\n  broken: [\n");
            provider.PollForChanges();
            Assert.True(provider.TryGet("Epg:UpdateIntervalMinutes", out string? afterBroken));
            Assert.Equal("45", afterBroken);

            provider.Dispose();
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(directory)!, recursive: true);
        }
    }

    private static string FindConfigTemplate()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string candidate = Path.Combine(directory.FullName, "config", "config.yml.template");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException("config/config.yml.template was not found above the test output directory.");
    }
}
