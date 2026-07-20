using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using TNLAStation.Application.Abstractions;
using TNLAStation.Application.Models;
using TNLAStation.Domain;
using TNLAStation.Infrastructure.Configuration;
using TNLAStation.Infrastructure.Persistence;
using TNLAStation.Infrastructure.Repositories;

namespace TNLAStation.Infrastructure.Tests;

/// <summary>
/// 番組表の永続化を実際の PostgreSQL に対して確認する。migration、制約、並べ替え、
/// スナップショットの入れ替えは SQL を通したときにしか壊れ方が分からない。
/// </summary>
public sealed class PostgresEpgRepositoryTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 20, 12, 0, 0, TimeSpan.Zero);

    [PostgresFact]
    public async Task MigrationCreatesASchemaTheRepositoryCanRoundTrip()
    {
        await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
        PostgresEpgRepository repository = CreateRepository(database);

        await repository.ReplaceSnapshotAsync(
            new EpgSnapshot([CreateChannel()], [CreateProgram()], Now),
            CancellationToken.None);

        EpgChannel channel = Assert.Single(await repository.ListChannelsAsync(CancellationToken.None));
        Assert.Equal("ＮＨＫ総合１・東京", channel.Name);
        Assert.Equal("NHK総合1・東京", channel.HalfWidthName);
        Assert.True(channel.HasLogoData);

        EpgProgram? program = await repository.GetProgramAsync(1, CancellationToken.None);
        Assert.NotNull(program);
        Assert.Equal("テスト番組", program!.Name);
        Assert.Equal("固定データ", program.RawExtended!["補足"]);
    }

    [PostgresFact]
    public async Task ReplacingTheSnapshotRemovesProgramsThatAreNoLongerBroadcast()
    {
        await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
        PostgresEpgRepository repository = CreateRepository(database);

        await repository.ReplaceSnapshotAsync(
            new EpgSnapshot([CreateChannel()], [CreateProgram(1), CreateProgram(2, eventId: 2)], Now),
            CancellationToken.None);
        await repository.ReplaceSnapshotAsync(
            new EpgSnapshot([CreateChannel()], [CreateProgram(2, eventId: 2)], Now),
            CancellationToken.None);

        Assert.Null(await repository.GetProgramAsync(1, CancellationToken.None));
        Assert.NotNull(await repository.GetProgramAsync(2, CancellationToken.None));
    }

    [PostgresFact]
    public async Task ApplyChangesUpsertsProgramsAndDeletesTheOnesMirakurunDropped()
    {
        await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
        PostgresEpgRepository repository = CreateRepository(database);
        await repository.ReplaceSnapshotAsync(
            new EpgSnapshot([CreateChannel()], [CreateProgram(1), CreateProgram(2, eventId: 2)], Now),
            CancellationToken.None);

        EpgProgram renamed = CreateProgram(1) with { Name = "差し替え後", HalfWidthName = "差し替え後" };
        await repository.ApplyChangesAsync([], [renamed], [2], Now, CancellationToken.None);

        EpgProgram? updated = await repository.GetProgramAsync(1, CancellationToken.None);
        Assert.Equal("差し替え後", updated!.Name);
        Assert.Null(await repository.GetProgramAsync(2, CancellationToken.None));
    }

    [PostgresFact]
    public async Task EndedProgramsAreDeletedByThreshold()
    {
        await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
        PostgresEpgRepository repository = CreateRepository(database);
        EpgProgram past = CreateProgram(1) with { StartAt = Now.AddHours(-3), EndAt = Now.AddHours(-2) };
        await repository.ReplaceSnapshotAsync(
            new EpgSnapshot([CreateChannel()], [past, CreateProgram(2, eventId: 2)], Now),
            CancellationToken.None);

        await repository.DeleteProgramsEndingBeforeAsync(Now, CancellationToken.None);

        Assert.Null(await repository.GetProgramAsync(1, CancellationToken.None));
        Assert.NotNull(await repository.GetProgramAsync(2, CancellationToken.None));
    }

    [PostgresFact]
    public async Task ChannelsComeBackInTheConfiguredDisplayOrder()
    {
        await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
        PostgresEpgRepository repository = CreateRepository(database, new EpgOptions { ChannelOrder = [200] });

        await repository.ReplaceSnapshotAsync(
            new EpgSnapshot(
                [
                    CreateChannel(100, serviceId: 1, remoteControlKeyId: 1),
                    CreateChannel(200, serviceId: 2, remoteControlKeyId: 4),
                ],
                [],
                Now),
            CancellationToken.None);

        IReadOnlyList<EpgChannel> channels = await repository.ListChannelsAsync(CancellationToken.None);

        Assert.Equal([200L, 100L], channels.Select(channel => channel.Id).ToArray());
    }

    [PostgresFact]
    public async Task ScheduleQueriesIncludeProgramsOverlappingBothEnds()
    {
        await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
        PostgresEpgRepository repository = CreateRepository(database);
        EpgProgram program = CreateProgram(1) with { StartAt = Now, EndAt = Now.AddHours(1) };
        await repository.ReplaceSnapshotAsync(
            new EpgSnapshot([CreateChannel()], [program], Now),
            CancellationToken.None);

        Assert.Single(await repository.FindProgramsAsync(
            new EpgScheduleQuery(Now.AddHours(1), Now.AddHours(2), ["GR"]),
            CancellationToken.None));
        Assert.Single(await repository.FindProgramsAsync(
            new EpgScheduleQuery(Now.AddHours(-1), Now, ["GR"]),
            CancellationToken.None));
        Assert.Empty(await repository.FindProgramsAsync(
            new EpgScheduleQuery(Now.AddHours(2), Now.AddHours(3), ["GR"]),
            CancellationToken.None));
    }

    [PostgresFact]
    public async Task SearchAppliesTheSamePolicyAsTheInMemoryStore()
    {
        await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
        PostgresEpgRepository repository = CreateRepository(database);
        EpgProgram program = CreateProgram(1) with
        {
            StartAt = Now.AddHours(1),
            EndAt = Now.AddHours(2),
            Name = "ＮＨＫ　ニュース",
            HalfWidthName = "NHK ニュース",
        };
        await repository.ReplaceSnapshotAsync(
            new EpgSnapshot([CreateChannel()], [program], Now),
            CancellationToken.None);

        Assert.Single(await repository.SearchProgramsAsync(
            new EpgSearchQuery(Keyword: "ニュース", Name: true),
            CancellationToken.None));
        Assert.Empty(await repository.SearchProgramsAsync(
            new EpgSearchQuery(Keyword: "天気", Name: true),
            CancellationToken.None));
    }

    [PostgresFact]
    public async Task OnlyOneInstanceHoldsTheSynchronizationLease()
    {
        await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
        var first = new PostgresEpgSyncLeaseProvider(database.ConnectionString);
        var second = new PostgresEpgSyncLeaseProvider(database.ConnectionString);

        IAsyncDisposable? held = await first.TryAcquireAsync(CancellationToken.None);
        Assert.NotNull(held);
        Assert.Null(await second.TryAcquireAsync(CancellationToken.None));

        await held!.DisposeAsync();
        IAsyncDisposable? afterRelease = await second.TryAcquireAsync(CancellationToken.None);
        Assert.NotNull(afterRelease);
        await afterRelease!.DisposeAsync();
    }

    private static PostgresEpgRepository CreateRepository(
        PostgresTestDatabase database,
        EpgOptions? options = null) =>
        // 検索は「終わった番組を返さない」ため現在時刻に依存する。fixture の日付を基準に時計を
        // 固定しておかないと、日付が変わった翌日に落ちる。
        new(database.ContextFactory, Options.Create(options ?? new EpgOptions()), new FakeTimeProvider(Now));

    private static EpgChannel CreateChannel(
        long id = 3_273_601_024,
        int serviceId = 1024,
        int? remoteControlKeyId = 1) =>
        new(
            id,
            serviceId,
            32736,
            "ＮＨＫ総合１・東京",
            "NHK総合1・東京",
            remoteControlKeyId,
            HasLogoData: true,
            ChannelTypeId: 0,
            ChannelType: "GR",
            Channel: "27",
            ServiceType: 1);

    private static EpgProgram CreateProgram(long id = 1, long eventId = 1) =>
        new(
            id,
            UpdateTime: Now,
            ChannelId: 3_273_601_024,
            EventId: eventId,
            ServiceId: 1024,
            NetworkId: 32736,
            StartAt: Now.AddHours(1),
            EndAt: Now.AddHours(2),
            StartHour: 22,
            Week: (int)DayOfWeek.Monday,
            DurationMilliseconds: 3_600_000,
            IsFree: true,
            Name: "テスト番組",
            HalfWidthName: "テスト番組",
            ShortName: "テスト番組",
            ChannelType: "GR",
            Channel: "27",
            RawExtended: new Dictionary<string, string> { ["補足"] = "固定データ" });
}
