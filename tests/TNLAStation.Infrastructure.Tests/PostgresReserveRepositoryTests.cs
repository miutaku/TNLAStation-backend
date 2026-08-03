using TNLAStation.Application.Abstractions;
using TNLAStation.Application.Models;
using TNLAStation.Domain;
using TNLAStation.Infrastructure.Persistence;
using TNLAStation.Infrastructure.Repositories;

namespace TNLAStation.Infrastructure.Tests;

/// <summary>
/// 予約の永続化。手動予約と生成結果と人の意思を別々に持つ設計は、実際に SQL を通したときに
/// しか壊れ方が分からない。番組表との結合、削除が除外に化ける経路、生成のたびに人の意思が
/// 残ることを、実データベースに対して確かめる。
/// </summary>
public sealed class PostgresReserveRepositoryTests
{
    [PostgresFact]
    public async Task AManualReserveOnAProgramTakesItsDetailsFromTheSchedule()
    {
        await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
        await RecordingTestData.SeedEpgAsync(database, RecordingTestData.CreateProgram(1, name: "夜のニュース"));
        PostgresReserveRepository repository = Create(database);

        await repository.AddAsync(ManualOnProgram(programId: 1), CancellationToken.None);
        await PublishReservesAsync(repository);

        // 予約は番組の詳細を持たず、読むときに番組表から取る。放送までに番組が変わっても
        // 追従できる形になっているかを確かめる。
        Reservation reserve = await SingleReserveAsync(repository);
        Assert.Equal("夜のニュース", reserve.Name);
        Assert.Equal(RecordingTestData.ChannelId, reserve.ChannelId);
        Assert.False(reserve.IsTimeSpecified);
        Assert.Equal("番組の概要", reserve.Description);
    }

    [PostgresFact]
    public async Task ATimeSpecifiedReserveKeepsItsOwnDetails()
    {
        await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
        await RecordingTestData.SeedEpgAsync(database);
        PostgresReserveRepository repository = Create(database);

        var command = new CreateReserveCommand(
            AllowEndLack: true,
            ProgramId: null,
            TimeSpecified: new TimeSpecifiedReserve(
                "手動の番組",
                RecordingTestData.ChannelId,
                RecordingTestData.Now.ToUnixTimeMilliseconds(),
                RecordingTestData.Now.AddHours(1).ToUnixTimeMilliseconds()),
            Tags: null,
            Save: null,
            Encode: null);

        await repository.AddAsync(command, CancellationToken.None);
        await PublishReservesAsync(repository);

        Reservation reserve = await SingleReserveAsync(repository);
        Assert.Equal("手動の番組", reserve.Name);
        Assert.True(reserve.IsTimeSpecified);
    }

    [PostgresFact]
    public async Task ManualReservesSurviveRegeneration()
    {
        await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
        await RecordingTestData.SeedEpgAsync(database, RecordingTestData.CreateProgram(1));
        PostgresReserveRepository repository = Create(database);
        long manualId = await repository.AddAsync(ManualOnProgram(programId: 1), CancellationToken.None);

        // 生成は予約表を書き換える。手動予約そのものは別の表なので、作り直しても残る。
        IReadOnlyList<ManualReserve> manuals = await repository.ListManualReservesAsync(CancellationToken.None);
        await repository.ReplaceAsync(
            [.. manuals.Select(manual => Assign(FromManual(manual), tuner: 0))],
            RecordingTestData.Now,
            CancellationToken.None);
        await repository.ReplaceAsync(
            [.. manuals.Select(manual => Assign(FromManual(manual), tuner: 0))],
            RecordingTestData.Now,
            CancellationToken.None);

        ManualReserve survivor = Assert.Single(await repository.ListManualReservesAsync(CancellationToken.None));
        Assert.Equal(manualId, survivor.Id);
        Assert.Single((await repository.ListAsync(new ReserveQuery(false), CancellationToken.None)).Items);
    }

    /// <summary>
    /// 予約 id は編集・削除の宛先。振り直すと再生成を挟んだだけで ReservationIsNotFound になる。
    /// </summary>
    [PostgresFact]
    public async Task RegenerationKeepsTheReserveIdOfEveryProgramThatIsStillReserved()
    {
        await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
        await RecordingTestData.SeedEpgAsync(
            database,
            RecordingTestData.CreateProgram(1, eventId: 1),
            RecordingTestData.CreateProgram(2, eventId: 2));
        PostgresReserveRepository repository = Create(database);
        await repository.ReplaceAsync(
            [RuleAssignment(ruleId: 5, programId: 1), RuleAssignment(ruleId: 5, programId: 2)],
            RecordingTestData.Now,
            CancellationToken.None);
        Dictionary<long, long> before = (await repository.ListAsync(new ReserveQuery(false), CancellationToken.None))
            .Items.ToDictionary(item => item.ProgramId!.Value, item => item.Id);

        // 2 番目が番組表から落ち、3 番目が増えた回。残った 1 番目の宛先は変わってはいけない。
        await RecordingTestData.SeedEpgAsync(
            database,
            RecordingTestData.CreateProgram(1, eventId: 1),
            RecordingTestData.CreateProgram(3, eventId: 3));
        await repository.ReplaceAsync(
            [RuleAssignment(ruleId: 5, programId: 1), RuleAssignment(ruleId: 5, programId: 3)],
            RecordingTestData.Now.AddMinutes(10),
            CancellationToken.None);

        Dictionary<long, long> after = (await repository.ListAsync(new ReserveQuery(false), CancellationToken.None))
            .Items.ToDictionary(item => item.ProgramId!.Value, item => item.Id);
        Assert.Equal(before[1], after[1]);
        Assert.DoesNotContain(2L, after.Keys);
        Assert.DoesNotContain(after[3], before.Values);
    }

    [PostgresFact]
    public async Task DeletingAManualReserveRemovesIt()
    {
        await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
        await RecordingTestData.SeedEpgAsync(database, RecordingTestData.CreateProgram(1));
        PostgresReserveRepository repository = Create(database);
        long manualId = await repository.AddAsync(ManualOnProgram(programId: 1), CancellationToken.None);
        await PublishReservesAsync(repository);

        long reserveId = (await SingleReserveAsync(repository)).Id;
        Assert.True(await repository.DeleteAsync(reserveId, CancellationToken.None));

        Assert.Empty(await repository.ListManualReservesAsync(CancellationToken.None));
    }

    [PostgresFact]
    public async Task DeletingARuleReserveTurnsIntoASkipThatSurvivesRegeneration()
    {
        await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
        await RecordingTestData.SeedEpgAsync(database, RecordingTestData.CreateProgram(1));
        PostgresReserveRepository repository = Create(database);

        // ルールが作った予約は消してもすぐ作り直される。消えたように見せず、録らないという
        // 指定として残す。その指定が生成をまたいで効くことを確かめる。
        await repository.ReplaceAsync([RuleAssignment(ruleId: 5, programId: 1)], RecordingTestData.Now, CancellationToken.None);
        Reservation reserve = await SingleReserveAsync(repository);
        Assert.True(await repository.DeleteAsync(reserve.Id, CancellationToken.None));

        ReserveStates states = await repository.ListStatesAsync(CancellationToken.None);
        Assert.Contains("rule:5:1", states.Skipped);

        // 生成は、残った意思を読んで組み立てた予約を書き込む。その意思がまた反映されることを
        // 確かめたいので、生成側と同じ手順で作り直す。
        await repository.ReplaceAsync(
            [RuleAssignment(ruleId: 5, programId: 1, isSkip: states.Skipped.Contains("rule:5:1"))],
            RecordingTestData.Now,
            CancellationToken.None);
        Assert.True((await SingleReserveAsync(repository)).IsSkip);
    }

    [PostgresFact]
    public async Task ClearingAnOverlapSurvivesRegeneration()
    {
        await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
        await RecordingTestData.SeedEpgAsync(database, RecordingTestData.CreateProgram(1));
        PostgresReserveRepository repository = Create(database);
        await repository.ReplaceAsync(
            [RuleAssignment(ruleId: 5, programId: 1, isOverlap: true)],
            RecordingTestData.Now,
            CancellationToken.None);

        Reservation reserve = await SingleReserveAsync(repository);
        Assert.True(await repository.ClearOverlapAsync(reserve.Id, CancellationToken.None));

        ReserveStates states = await repository.ListStatesAsync(CancellationToken.None);
        Assert.Contains("rule:5:1", states.OverlapCleared);
    }

    [PostgresFact]
    public async Task EditingARuleReserveSurvivesRegeneration()
    {
        await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
        await RecordingTestData.SeedEpgAsync(database, RecordingTestData.CreateProgram(1));
        PostgresReserveRepository repository = Create(database);
        ReserveAssignment assignment = RuleAssignment(ruleId: 5, programId: 1);
        await repository.ReplaceAsync([assignment], RecordingTestData.Now, CancellationToken.None);

        Reservation original = await SingleReserveAsync(repository);
        var edit = new CreateReserveCommand(
            AllowEndLack: false,
            ProgramId: original.ProgramId,
            TimeSpecified: null,
            Tags: [10, 20],
            Save: new ReserveSaveSettings("archive", "drama", "%TITLE%"),
            Encode: new ReserveEncodeSettings(
                "H.264",
                "encoded",
                "main",
                null,
                null,
                null,
                null,
                null,
                null,
                true));

        Assert.True(await repository.UpdateAsync(original.Id, edit, CancellationToken.None));
        Reservation updated = await SingleReserveAsync(repository);
        Assert.False(updated.AllowEndLack);
        Assert.Equal([10, 20], updated.Tags);
        Assert.Equal("archive", updated.ParentDirectoryName);
        Assert.Equal("H.264", updated.EncodeMode1);
        Assert.True(updated.IsDeleteOriginalAfterEncode);

        await repository.ReplaceAsync([assignment], RecordingTestData.Now, CancellationToken.None);
        Reservation regenerated = await SingleReserveAsync(repository);
        Assert.False(regenerated.AllowEndLack);
        Assert.Equal([10, 20], regenerated.Tags);
        Assert.Equal("H.264", regenerated.EncodeMode1);
    }

    [PostgresFact]
    public async Task ListFiltersByType()
    {
        await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
        await RecordingTestData.SeedEpgAsync(
            database,
            RecordingTestData.CreateProgram(1),
            RecordingTestData.CreateProgram(2, eventId: 2, startAt: RecordingTestData.Now.AddHours(3)));
        PostgresReserveRepository repository = Create(database);
        await repository.ReplaceAsync(
            [
                RuleAssignment(ruleId: 5, programId: 1),
                RuleAssignment(ruleId: 5, programId: 2, tuner: null, isConflict: true),
            ],
            RecordingTestData.Now,
            CancellationToken.None);

        Assert.Single((await repository.ListAsync(new ReserveQuery(false, Type: "normal"), CancellationToken.None)).Items);
        Assert.Single((await repository.ListAsync(new ReserveQuery(false, Type: "conflict"), CancellationToken.None)).Items);
        Assert.Equal(2, (await repository.ListAsync(new ReserveQuery(false), CancellationToken.None)).Total);
    }

    /// <summary>
    /// EPGStation は録画が終わった予約を Recorded へ移して Reserve から消す。この実装はまだその
    /// 移動をしないので、一覧が読むときに終了済みを外すことで見た目を揃えている。
    /// </summary>
    [PostgresFact]
    public async Task ListExcludesReservesThatHaveAlreadyEnded()
    {
        await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
        await RecordingTestData.SeedEpgAsync(database);
        PostgresReserveRepository repository = Create(database);

        var ended = new ReserveTarget(
            ReserveSource.Rule,
            RecordingTestData.ChannelId,
            "GR",
            RecordingTestData.Now.AddHours(-3),
            RecordingTestData.Now.AddHours(-2),
            "終わった番組",
            ProgramId: 101,
            RuleId: 5);
        var upcoming = new ReserveTarget(
            ReserveSource.Rule,
            RecordingTestData.ChannelId,
            "GR",
            RecordingTestData.Now.AddHours(1),
            RecordingTestData.Now.AddHours(2),
            "これからの番組",
            ProgramId: 102,
            RuleId: 5);
        await repository.ReplaceAsync(
            [new ReserveAssignment(ended, TunerIndex: null), new ReserveAssignment(upcoming, TunerIndex: 0)],
            RecordingTestData.Now,
            CancellationToken.None);

        Reservation remaining = Assert.Single(
            (await repository.ListAsync(new ReserveQuery(false), CancellationToken.None)).Items);
        Assert.Equal("これからの番組", remaining.Name);
    }

    [PostgresFact]
    public async Task ListSearchesProgramDetailsAndStructuredFields()
    {
        await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
        EpgProgram drama = RecordingTestData.CreateProgram(1, name: "夜のドラマ") with
        {
            Description = "深夜の特集です",
            HalfWidthDescription = "深夜の特集です",
            Extended = "出演者情報",
            HalfWidthExtended = "出演者情報",
            Genre1 = 7,
        };
        EpgProgram news = RecordingTestData.CreateProgram(
            2,
            eventId: 2,
            name: "朝のニュース",
            startAt: RecordingTestData.Now.AddHours(3)) with
        {
            Genre1 = 0,
        };
        await RecordingTestData.SeedEpgAsync(database, drama, news);

        PostgresReserveRepository repository = Create(database);
        await repository.AddAsync(ManualOnProgram(programId: 1), CancellationToken.None);
        ManualReserve manual = Assert.Single(await repository.ListManualReservesAsync(CancellationToken.None));
        await repository.ReplaceAsync(
            [
                Assign(FromManual(manual), tuner: 0),
                RuleAssignment(ruleId: 5, programId: 2),
            ],
            RecordingTestData.Now,
            CancellationToken.None);

        Assert.Equal(
            "夜のドラマ",
            Assert.Single((await repository.ListAsync(
                new ReserveQuery(false, Keyword: "深夜"),
                CancellationToken.None)).Items).Name);
        Assert.Equal(
            "夜のドラマ",
            Assert.Single((await repository.ListAsync(
                new ReserveQuery(false, Genre: 7),
                CancellationToken.None)).Items).Name);
        Assert.Null(Assert.Single((await repository.ListAsync(
            new ReserveQuery(false, RuleId: 0),
            CancellationToken.None)).Items).RuleId);
        Assert.Equal(5, Assert.Single((await repository.ListAsync(
            new ReserveQuery(false, RuleId: 5),
            CancellationToken.None)).Items).RuleId);
        Assert.Equal(2, (await repository.ListAsync(
            new ReserveQuery(false, ChannelId: RecordingTestData.ChannelId),
            CancellationToken.None)).Total);
        Assert.Empty((await repository.ListAsync(
            new ReserveQuery(false, ChannelId: -1),
            CancellationToken.None)).Items);
    }

    [PostgresFact]
    public async Task RuleReservationsIncludeTheCurrentDisplayNameAndHandleMissingRules()
    {
        await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
        await RecordingTestData.SeedEpgAsync(
            database,
            RecordingTestData.CreateProgram(1),
            RecordingTestData.CreateProgram(2, eventId: 2, startAt: RecordingTestData.Now.AddHours(3)));

        long namedRuleId;
        long unnamedRuleId;
        await using (EpgDbContext context = await database.ContextFactory.CreateDbContextAsync())
        {
            var namedRule = new RuleEntity { DisplayName = " 週末ドラマ " };
            var unnamedRule = new RuleEntity { Keyword = " アニメ " };
            context.Rules.AddRange(namedRule, unnamedRule);
            await context.SaveChangesAsync();
            namedRuleId = namedRule.Id;
            unnamedRuleId = unnamedRule.Id;
        }

        PostgresReserveRepository repository = Create(database);
        await repository.ReplaceAsync(
            [
                RuleAssignment(ruleId: namedRuleId, programId: 1),
                RuleAssignment(ruleId: unnamedRuleId, programId: 2),
            ],
            RecordingTestData.Now,
            CancellationToken.None);

        Page<Reservation> page = await repository.ListAsync(new ReserveQuery(false), CancellationToken.None);
        Assert.Equal("週末ドラマ", page.Items.Single(item => item.RuleId == namedRuleId).RuleName);
        Assert.Equal("無題のルール", page.Items.Single(item => item.RuleId == unnamedRuleId).RuleName);

        await using (EpgDbContext context = await database.ContextFactory.CreateDbContextAsync())
        {
            RuleEntity namedRule = await context.Rules.FindAsync(namedRuleId)
                ?? throw new InvalidOperationException("Seeded rule was not found.");
            context.Rules.Remove(namedRule);
            await context.SaveChangesAsync();
        }

        page = await repository.ListAsync(new ReserveQuery(false), CancellationToken.None);
        Assert.Null(page.Items.Single(item => item.RuleId == namedRuleId).RuleName);
    }

    private static PostgresReserveRepository Create(PostgresTestDatabase database) =>
        new(database.ContextFactory, RecordingTestData.Clock());

    private static CreateReserveCommand ManualOnProgram(long programId) =>
        new(
            AllowEndLack: true,
            ProgramId: programId,
            TimeSpecified: null,
            Tags: null,
            Save: null,
            Encode: null);

    private static async Task PublishReservesAsync(PostgresReserveRepository repository)
    {
        IReadOnlyList<ManualReserve> manuals = await repository.ListManualReservesAsync(CancellationToken.None);
        await repository.ReplaceAsync(
            [.. manuals.Select(manual => Assign(FromManual(manual), tuner: 0))],
            RecordingTestData.Now,
            CancellationToken.None);
    }

    private static async Task<Reservation> SingleReserveAsync(PostgresReserveRepository repository)
    {
        Page<Reservation> page = await repository.ListAsync(new ReserveQuery(false), CancellationToken.None);
        return Assert.Single(page.Items);
    }

    private static ReserveTarget FromManual(ManualReserve manual) =>
        new(
            ReserveSource.Manual,
            manual.ChannelId,
            manual.ChannelType,
            manual.StartAt,
            manual.EndAt,
            manual.Name,
            manual.ProgramId,
            ManualReserveId: manual.Id);

    private static ReserveAssignment RuleAssignment(
        long ruleId,
        long programId,
        int? tuner = 0,
        bool isConflict = false,
        bool isOverlap = false,
        bool isSkip = false)
    {
        var target = new ReserveTarget(
            ReserveSource.Rule,
            RecordingTestData.ChannelId,
            "GR",
            RecordingTestData.Now.AddHours(1),
            RecordingTestData.Now.AddHours(2),
            "ルールの番組",
            programId,
            RuleId: ruleId,
            IsSkip: isSkip,
            IsOverlap: isOverlap);
        return new ReserveAssignment(target, isConflict || isOverlap || isSkip ? null : tuner);
    }

    private static ReserveAssignment Assign(ReserveTarget target, int? tuner) => new(target, tuner);
}
