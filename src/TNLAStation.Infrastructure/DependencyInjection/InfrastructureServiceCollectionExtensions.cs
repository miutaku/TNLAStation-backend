using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TNLAStation.Application.Abstractions;
using TNLAStation.Infrastructure.CommandHooks;
using TNLAStation.Infrastructure.Configuration;
using TNLAStation.Infrastructure.Configuration.EpgStation;
using TNLAStation.Infrastructure.Kodi;
using TNLAStation.Infrastructure.Mirakurun;
using TNLAStation.Infrastructure.Persistence;
using TNLAStation.Infrastructure.Recording;
using TNLAStation.Infrastructure.Repositories;
using TNLAStation.Infrastructure.Reserves;
using TNLAStation.Infrastructure.Storage;
using TNLAStation.Infrastructure.Streaming;
using TNLAStation.Infrastructure.Transcoding;

namespace TNLAStation.Infrastructure.DependencyInjection;

/// <summary>
/// Binds the infrastructure adapters an installation actually has. PostgreSQL and Mirakurun are
/// optional so that contract tests, and a first run without a tuner, keep the same HTTP surface
/// with in-memory adapters instead of failing at startup.
/// </summary>
public static class InfrastructureServiceCollectionExtensions
{
    public const string PostgresConnectionName = "PostgreSQL";

    public static IServiceCollection AddTnlaStationInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        // 依存が足りないまま「静かに動かない」構成で起動しないよう、ここで止める。
        // 縮退運転は AllowDegradedStartup=true を明示した構成でだけ許す。
        StartupRequirements.Validate(configuration, configuration["EpgStationConfig:LoadedPath"]);

        services.TryAddTimeProvider();
        services.Configure<ServerOptions>(configuration.GetSection(ServerOptions.SectionName));
        services.Configure<ApiOptions>(configuration.GetSection(ApiOptions.SectionName));
        services.Configure<EpgOptions>(configuration.GetSection(EpgOptions.SectionName));
        services.Configure<MirakurunOptions>(configuration.GetSection(MirakurunOptions.SectionName));
        services.Configure<StorageOptions>(configuration.GetSection(StorageOptions.SectionName));
        services.Configure<StreamingOptions>(configuration.GetSection(StreamingOptions.SectionName));
        services.Configure<FfmpegWorkerOptions>(configuration.GetSection(FfmpegWorkerOptions.SectionName));
        services.Configure<ReserveOptions>(configuration.GetSection(ReserveOptions.SectionName));
        services.Configure<RecordingOptions>(configuration.GetSection(RecordingOptions.SectionName));
        services.Configure<EncodeOptions>(configuration.GetSection(EncodeOptions.SectionName));
        services.Configure<ThumbnailOptions>(configuration.GetSection(ThumbnailOptions.SectionName));
        services.Configure<KodiOptions>(configuration.GetSection(KodiOptions.SectionName));
        services.Configure<UrlSchemeOptions>(configuration.GetSection(UrlSchemeOptions.SectionName));
        services.Configure<CommandHookOptions>(configuration.GetSection(CommandHookOptions.SectionName));
        services.AddSingleton<ICommandHookRunner, CommandHookRunner>();

        // socket.io を持つ構成 (API ホスト) では Api 側が差し替える。持たない構成 (worker、
        // 一部の試験) では何もしない実装で動かす。
        if (services.All(descriptor => descriptor.ServiceType != typeof(IClientNotifier)))
        {
            services.AddSingleton<IClientNotifier>(NullClientNotifier.Instance);
        }

        services.AddSingleton<IEpgStationConfigAccessor, EpgStationConfigAccessor>();
        services.AddSingleton<IConfigRepository, EpgStationConfigRepository>();
        services.AddSingleton<IStorageRepository, RecordedDirectoryStorageRepository>();
        services.AddSingleton<IVersionRepository, MockVersionRepository>();
        services.AddHttpClient(FfmpegWorkerClientName, ConfigureFfmpegWorkerClient);
        services.AddHttpClient<IMediaProbe, RemoteMediaProbe>(ConfigureFfmpegWorkerClient);
        services.AddSingleton<IRecordingJobRegistry, RecordingJobRegistry>();
        services.AddSingleton<RecordingScheduleSignal>();
        services.AddSingleton<IRecordingScheduleTrigger>(provider =>
            provider.GetRequiredService<RecordingScheduleSignal>());
        services.AddHttpClient<IKodiClient, KodiClient>();

        AddEpgStore(services, configuration.GetConnectionString(PostgresConnectionName));
        AddRuleStore(services, configuration.GetConnectionString(PostgresConnectionName));
        AddMirakurun(services, configuration.GetSection(MirakurunOptions.SectionName).Get<MirakurunOptions>());

        return services;
    }

    private static void AddEpgStore(IServiceCollection services, string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            services.AddSingleton<InMemoryEpgRepository>();
            services.AddSingleton<IEpgRepository>(provider => provider.GetRequiredService<InMemoryEpgRepository>());
            services.AddSingleton<IEpgStore>(provider => provider.GetRequiredService<InMemoryEpgRepository>());
            services.AddSingleton<IEpgSyncLeaseProvider, InMemoryEpgSyncLeaseProvider>();
            services.AddSingleton<IReserveGenerationLeaseProvider, InMemoryEpgSyncLeaseProvider>();
            services.AddSingleton<IRecordingLeaseProvider, InMemoryEpgSyncLeaseProvider>();
            return;
        }

        services.AddDbContextFactory<EpgDbContext>(options => options.UseNpgsql(
            connectionString,
            npgsql => npgsql.MigrationsAssembly(typeof(EpgDbContext).Assembly.FullName)));
        services.AddSingleton<PostgresEpgRepository>();
        services.AddSingleton<IEpgRepository>(provider => provider.GetRequiredService<PostgresEpgRepository>());
        services.AddSingleton<IEpgStore>(provider => provider.GetRequiredService<PostgresEpgRepository>());
        services.AddSingleton<IEpgSyncLeaseProvider>(_ => new PostgresEpgSyncLeaseProvider(connectionString));
        services.AddSingleton<IReserveGenerationLeaseProvider>(_ => new PostgresReserveLeaseProvider(connectionString));
        services.AddSingleton<IRecordingLeaseProvider>(_ => new PostgresRecordingLeaseProvider(connectionString));
    }

    private static void AddRuleStore(IServiceCollection services, string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            services.AddSingleton<IRuleRepository, InMemoryRuleRepository>();
            services.AddSingleton<IReserveRepository, InMemoryReserveRepository>();
            // 保存先を持たない構成。録画は残せないので、一覧はどれも「いま空」を返す。
            services.AddSingleton<IRecordedRepository, InMemoryRecordedRepository>();
            services.AddSingleton<IRecordingRepository, EmptyRecordingRepository>();
            services.AddSingleton<IRecordedTagRepository, EmptyRecordedTagRepository>();
            services.AddSingleton<UnavailableRecordedItemRepository>();
            services.AddSingleton<IRecordedItemRepository>(provider =>
                provider.GetRequiredService<UnavailableRecordedItemRepository>());
            services.AddSingleton<IRecordedTagWriteRepository>(provider =>
                provider.GetRequiredService<UnavailableRecordedItemRepository>());
            services.AddSingleton<EmptyVideoFileRepository>();
            services.AddSingleton<IVideoFileRepository>(provider =>
                provider.GetRequiredService<EmptyVideoFileRepository>());
            services.AddSingleton<IVideoFileUploadRepository>(provider =>
                provider.GetRequiredService<EmptyVideoFileRepository>());
            services.AddSingleton<IEncodeQueueRepository, EmptyEncodeQueueRepository>();
            services.AddSingleton<IDropLogRepository, EmptyDropLogRepository>();
            services.AddSingleton<IEncodeTaskList, UnavailableEncodeTaskList>();
            services.AddSingleton<IThumbnailService, UnavailableThumbnailService>();
            services.AddSingleton<IRecordingStopService, UnavailableRecordingStopService>();
            services.AddSingleton<IRecordedHistoryStore, InMemoryRecordedHistoryStore>();
            return;
        }

        services.AddSingleton<IRuleRepository, PostgresRuleRepository>();
        services.AddSingleton<PostgresRecordedRepository>();
        services.AddSingleton<IRecordedRepository>(provider =>
            provider.GetRequiredService<PostgresRecordedRepository>());
        services.AddSingleton<IRecordingRepository>(provider =>
            provider.GetRequiredService<PostgresRecordedRepository>());
        services.AddSingleton<IRecordedTagRepository>(provider =>
            provider.GetRequiredService<PostgresRecordedRepository>());
        services.AddSingleton<IRecordedItemRepository>(provider =>
            provider.GetRequiredService<PostgresRecordedRepository>());
        services.AddSingleton<IRecordedTagWriteRepository>(provider =>
            provider.GetRequiredService<PostgresRecordedRepository>());
        services.AddSingleton<PostgresRecordingStore>();
        services.AddSingleton<IRecordingStore>(provider => provider.GetRequiredService<PostgresRecordingStore>());
        services.AddSingleton<IDropLogRepository>(provider =>
            provider.GetRequiredService<PostgresRecordingStore>());
        services.AddSingleton<IRecordingStopService, PostgresRecordingStopService>();
        services.AddSingleton<PostgresVideoFileRepository>();
        services.AddSingleton<IVideoFileRepository>(provider =>
            provider.GetRequiredService<PostgresVideoFileRepository>());
        services.AddSingleton<IVideoFileUploadRepository>(provider =>
            provider.GetRequiredService<PostgresVideoFileRepository>());
        services.AddSingleton<IEncodeJobRegistry, EncodeJobRegistry>();
        services.AddSingleton<PostgresEncodeTaskList>();
        services.AddSingleton<IEncodeTaskList>(provider => provider.GetRequiredService<PostgresEncodeTaskList>());
        services.AddSingleton<IEncodeQueueRepository>(provider =>
            provider.GetRequiredService<PostgresEncodeTaskList>());
        services.AddHttpClient<IEncodeExecutor, RemoteEncodeExecutor>(ConfigureFfmpegWorkerClient);
        services.AddHostedService<EncodeWorker>();
        services.AddHostedService<StorageLimitHostedService>();
        services.AddHttpClient<IThumbnailService, RemoteThumbnailService>(ConfigureFfmpegWorkerClient);
        services.AddSingleton<PostgresReserveRepository>();
        services.AddSingleton<IReserveRepository>(provider =>
            provider.GetRequiredService<PostgresReserveRepository>());
        services.AddSingleton<IReserveStore>(provider =>
            provider.GetRequiredService<PostgresReserveRepository>());
        services.AddSingleton<IRecordedHistoryStore, PostgresRecordedHistoryStore>();
    }

    private static void AddMirakurun(IServiceCollection services, MirakurunOptions? options)
    {
        if (options?.IsConfigured != true)
        {
            services.AddSingleton<IChannelLogoProvider, InMemoryChannelLogoProvider>();
            services.AddSingleton<IBroadcastStatusProvider, EmptyBroadcastStatusProvider>();
            // チューナーに繋がっていない構成。視聴は始められないが、配信一覧は「いま 0 本」で正しい。
            services.AddSingleton<IStreamRepository, EmptyStreamRepository>();
            services.AddSingleton<ILiveStreamService, UnavailableLiveStreamService>();
            // チューナーの本数が分からないので予約は作れない。作り直しの依頼は空振りさせる。
            services.AddSingleton<IReserveGenerationTrigger, NoReserveGenerationTrigger>();
            return;
        }

        services.AddSingleton<MirakurunEpgMapper>();
        services.AddHttpClient<MirakurunClient>((provider, client) =>
            {
                MirakurunOptions current = provider.GetRequiredService<IOptions<MirakurunOptions>>().Value;
                client.BaseAddress = GetBaseAddress(current);
                // Per-request deadlines are applied by MirakurunClient; the event stream stays open.
                client.Timeout = Timeout.InfiniteTimeSpan;
            })
            .ConfigurePrimaryHttpMessageHandler(provider =>
            {
                MirakurunOptions current = provider.GetRequiredService<IOptions<MirakurunOptions>>().Value;
                return MirakurunConnection.CreateHandler(current.BaseUrl!, out _);
            });
        services.AddSingleton<IChannelLogoProvider>(provider =>
            provider.GetRequiredService<MirakurunClient>());
        services.AddSingleton<IMirakurunClient>(provider => provider.GetRequiredService<MirakurunClient>());
        services.AddSingleton<IBroadcastStatusProvider, MirakurunBroadcastStatusProvider>();
        services.AddHostedService<EpgSyncHostedService>();

        // 予約の生成にはチューナーの本数が要るので、Mirakurun がある構成でだけ動かす。
        services.AddSingleton<RecordingRecovery>();
        services.AddHostedService<RecordingScheduler>();
        services.AddSingleton<ReserveGenerator>();
        services.AddSingleton<IReserveGenerationTrigger>(provider =>
            provider.GetRequiredService<ReserveGenerator>());
        services.AddHostedService<ReserveGenerationHostedService>();

        // セッションの帳簿 (sessions dict・reaper) を 1 つに保つため、typed client の transient な
        // 生成には乗らず、IHttpClientFactory から取った HttpClient で明示的に singleton を組み立てる。
        // ILiveStreamService と IStreamRepository は同じインスタンスを指さないと、片方で開始した
        // 配信がもう片方の一覧に出てこなくなる。
        services.AddSingleton(provider => new RemoteLiveStreamService(
            provider.GetRequiredService<IHttpClientFactory>().CreateClient(FfmpegWorkerClientName),
            provider.GetRequiredService<IMirakurunClient>(),
            provider.GetRequiredService<IEpgRepository>(),
            provider.GetRequiredService<IVideoFileRepository>(),
            provider.GetRequiredService<IOptions<StreamingOptions>>(),
            provider.GetRequiredService<IEpgStationConfigAccessor>(),
            provider.GetRequiredService<IOptions<MirakurunOptions>>(),
            provider.GetRequiredService<TimeProvider>(),
            provider.GetRequiredService<ILogger<RemoteLiveStreamService>>()));
        services.AddSingleton<ILiveStreamService>(provider => provider.GetRequiredService<RemoteLiveStreamService>());
        services.AddSingleton<IStreamRepository>(provider => provider.GetRequiredService<RemoteLiveStreamService>());
    }

    private const string FfmpegWorkerClientName = "FfmpegWorker";

    /// <summary>
    /// ffmpeg-worker への typed client はどれも同じ接続先を見るので、設定の解決だけをここへ集約する。
    /// </summary>
    private static void ConfigureFfmpegWorkerClient(IServiceProvider provider, HttpClient client)
    {
        FfmpegWorkerOptions options = provider.GetRequiredService<IOptions<FfmpegWorkerOptions>>().Value;
        if (!string.IsNullOrWhiteSpace(options.BaseUrl))
        {
            client.BaseAddress = new Uri(options.BaseUrl);
        }
    }

    private static Uri GetBaseAddress(MirakurunOptions options)
    {
        MirakurunConnection.CreateHandler(options.BaseUrl!, out Uri baseAddress).Dispose();
        return baseAddress;
    }

    private static void TryAddTimeProvider(this IServiceCollection services)
    {
        if (services.All(descriptor => descriptor.ServiceType != typeof(TimeProvider)))
        {
            services.AddSingleton(TimeProvider.System);
        }
    }
}
