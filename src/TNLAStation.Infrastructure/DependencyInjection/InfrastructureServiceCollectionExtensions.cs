using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TNLAStation.Application.Abstractions;
using TNLAStation.Infrastructure.Configuration;
using TNLAStation.Infrastructure.Mirakurun;
using TNLAStation.Infrastructure.Persistence;
using TNLAStation.Infrastructure.Repositories;
using TNLAStation.Infrastructure.Encoding;
using TNLAStation.Infrastructure.Recording;
using TNLAStation.Infrastructure.Reserves;
using TNLAStation.Infrastructure.Streaming;

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

        services.TryAddTimeProvider();
        services.Configure<EpgOptions>(configuration.GetSection(EpgOptions.SectionName));
        services.Configure<MirakurunOptions>(configuration.GetSection(MirakurunOptions.SectionName));
        services.Configure<StorageOptions>(configuration.GetSection(StorageOptions.SectionName));
        services.Configure<StreamingOptions>(configuration.GetSection(StreamingOptions.SectionName));
        services.Configure<ReserveOptions>(configuration.GetSection(ReserveOptions.SectionName));
        services.Configure<RecordingOptions>(configuration.GetSection(RecordingOptions.SectionName));
        services.Configure<EncodeOptions>(configuration.GetSection(EncodeOptions.SectionName));

        services.AddSingleton<IConfigRepository, MockConfigRepository>();
        services.AddSingleton<IStorageRepository, RecordedDirectoryStorageRepository>();
        services.AddSingleton<IVersionRepository, MockVersionRepository>();
        services.AddSingleton<IMediaProbe, FfprobeMediaProbe>();

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
            services.AddSingleton<IVideoFileRepository, EmptyVideoFileRepository>();
            services.AddSingleton<IEncodeQueueRepository, EmptyEncodeQueueRepository>();
            services.AddSingleton<IEncodeTaskList, UnavailableEncodeTaskList>();
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
        services.AddSingleton<IRecordingStore, PostgresRecordingStore>();
        services.AddSingleton<IVideoFileRepository, PostgresVideoFileRepository>();
        services.AddSingleton<PostgresEncodeTaskList>();
        services.AddSingleton<IEncodeTaskList>(provider => provider.GetRequiredService<PostgresEncodeTaskList>());
        services.AddSingleton<IEncodeQueueRepository>(provider =>
            provider.GetRequiredService<PostgresEncodeTaskList>());
        services.AddHostedService<EncodeWorker>();
        services.AddSingleton<PostgresReserveRepository>();
        services.AddSingleton<IReserveRepository>(provider =>
            provider.GetRequiredService<PostgresReserveRepository>());
        services.AddSingleton<IReserveStore>(provider =>
            provider.GetRequiredService<PostgresReserveRepository>());
    }

    private static void AddMirakurun(IServiceCollection services, MirakurunOptions? options)
    {
        if (options?.IsConfigured != true)
        {
            services.AddSingleton<IChannelLogoProvider, InMemoryChannelLogoProvider>();
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
        services.AddHostedService<EpgSyncHostedService>();

        // 予約の生成にはチューナーの本数が要るので、Mirakurun がある構成でだけ動かす。
        services.AddHostedService<RecordingScheduler>();
        services.AddSingleton<ReserveGenerator>();
        services.AddSingleton<IReserveGenerationTrigger>(provider =>
            provider.GetRequiredService<ReserveGenerator>());
        services.AddHostedService<ReserveGenerationHostedService>();

        services.AddSingleton<HlsStreamManager>();
        services.AddSingleton<ILiveStreamService>(provider => provider.GetRequiredService<HlsStreamManager>());
        services.AddSingleton<IStreamRepository>(provider => provider.GetRequiredService<HlsStreamManager>());
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
