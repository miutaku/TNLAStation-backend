using TNLAStation.Application.Abstractions;
using TNLAStation.Application.Models;
using TNLAStation.Domain;
using TNLAStation.Infrastructure.Repositories;

namespace TNLAStation.Infrastructure.Tests;

/// <summary>
/// 録画済みと tag の永続化。保護が削除を止めること、tag の付け外し、録画中と録画済みを
/// 同じ表の状態で分けることを、実データベースに対して確かめる。
/// </summary>
public sealed class PostgresRecordedRepositoryTests
{
    [PostgresFact]
    public async Task ProtectedRecordingsCannotBeDeleted()
    {
        await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
        await RecordingTestData.SeedEpgAsync(database);
        (PostgresRecordedRepository repository, long id) = await CreateRecordedAsync(database);

        Assert.True(await repository.SetProtectedAsync(id, isProtected: true, CancellationToken.None));

        // 人が残すと決めたものを、こちらの判断で消さない。
        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => repository.DeleteAsync(id, CancellationToken.None).AsTask());
        Assert.Equal("RecordedIsProtected", error.Message);

        Assert.True(await repository.SetProtectedAsync(id, isProtected: false, CancellationToken.None));
        Assert.True(await repository.DeleteAsync(id, CancellationToken.None));
    }

    [PostgresFact]
    public async Task TagsCanBeAttachedAndDetached()
    {
        await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
        await RecordingTestData.SeedEpgAsync(database);
        (PostgresRecordedRepository repository, long recordedId) = await CreateRecordedAsync(database);

        long tagId = await repository.AddTagAsync("お気に入り", "#ff0000", CancellationToken.None);
        Assert.True(await repository.SetTagAsync(recordedId, tagId, attached: true, CancellationToken.None));

        RecordedProgram tagged = (await repository.GetAsync(recordedId, CancellationToken.None))!;
        RecordedTag tag = Assert.Single(tagged.Tags!);
        Assert.Equal("お気に入り", tag.Name);

        Assert.True(await repository.SetTagAsync(recordedId, tagId, attached: false, CancellationToken.None));
        RecordedProgram bare = (await repository.GetAsync(recordedId, CancellationToken.None))!;
        Assert.Empty(bare.Tags!);
    }

    [PostgresFact]
    public async Task DeletingATagDetachesItFromEveryRecording()
    {
        await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
        await RecordingTestData.SeedEpgAsync(database);
        (PostgresRecordedRepository repository, long recordedId) = await CreateRecordedAsync(database);
        long tagId = await repository.AddTagAsync("消す", "#00ff00", CancellationToken.None);
        await repository.SetTagAsync(recordedId, tagId, attached: true, CancellationToken.None);

        // tag を消すと、付いていた録画からも外れる。外れないと、存在しない tag を指す
        // 結び付きが残る。
        Assert.True(await repository.DeleteTagAsync(tagId, CancellationToken.None));

        RecordedProgram program = (await repository.GetAsync(recordedId, CancellationToken.None))!;
        Assert.Empty(program.Tags!);
        Assert.Empty((await repository.ListAsync(new RecordedTagQuery(), CancellationToken.None)).Items);
    }

    [PostgresFact]
    public async Task SetTagRejectsAMissingRecordingOrTag()
    {
        await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
        await RecordingTestData.SeedEpgAsync(database);
        (PostgresRecordedRepository repository, long recordedId) = await CreateRecordedAsync(database);
        long tagId = await repository.AddTagAsync("t", "#000000", CancellationToken.None);

        Assert.False(await repository.SetTagAsync(recordedId, tagId: 999, attached: true, CancellationToken.None));
        Assert.False(await repository.SetTagAsync(recordedId: 999, tagId, attached: true, CancellationToken.None));
    }

    [PostgresFact]
    public async Task RecordedSearchFiltersByKeyword()
    {
        await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
        await RecordingTestData.SeedEpgAsync(database);
        PostgresRecordedRepository repository = new(database.ContextFactory, RecordingTestData.Clock());
        await AddRecordedAsync(
            repository,
            "夜のニュース",
            description: "天気詳報",
            extended: "公式サイト",
            genre: 0,
            ruleId: 5);
        await AddRecordedAsync(repository, "昼の映画", description: "劇場作品", genre: 6);

        Page<RecordedProgram> hits = await repository.ListAsync(
            new RecordedQuery(IsHalfWidth: false, Keyword: "天気"),
            CancellationToken.None);

        Assert.Equal("夜のニュース", Assert.Single(hits.Items).Name);
        Assert.Equal("夜のニュース", Assert.Single((await repository.ListAsync(
            new RecordedQuery(IsHalfWidth: false, Keyword: "公式サイト"),
            CancellationToken.None)).Items).Name);
        Assert.Equal("夜のニュース", Assert.Single((await repository.ListAsync(
            new RecordedQuery(IsHalfWidth: false, RuleId: 5),
            CancellationToken.None)).Items).Name);
        Assert.Equal("昼の映画", Assert.Single((await repository.ListAsync(
            new RecordedQuery(IsHalfWidth: false, RuleId: 0),
            CancellationToken.None)).Items).Name);
        Assert.Equal("昼の映画", Assert.Single((await repository.ListAsync(
            new RecordedQuery(IsHalfWidth: false, Genre: 6),
            CancellationToken.None)).Items).Name);
    }

    /// <summary>
    /// 空き容量不足で消す候補の選び方。EPGStation の <c>RecordedDB.findOld()</c> は
    /// 「保護されていない行のうち id がいちばん小さいもの」で、保存先でも録画中かどうかでも
    /// 絞らない。<c>orderBy</c> を 2 回呼んでいて後勝ちになるため、開始時刻順ではなく登録順。
    /// </summary>
    [PostgresFact]
    public async Task TheStorageCleanupCandidateIsTheLowestUnprotectedRowId()
    {
        await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
        await RecordingTestData.SeedEpgAsync(database);
        PostgresRecordedRepository repository = new(database.ContextFactory, RecordingTestData.Clock());

        // 先に入れたほうが id が小さい。開始時刻は逆順にしておく。
        long first = await AddRecordedAsync(repository, "後で始まる番組", startOffsetHours: 5);
        long second = await AddRecordedAsync(repository, "先に始まる番組", startOffsetHours: 0);

        // 開始時刻順なら second が選ばれるが、EPGStation は id 順なので first。
        Assert.Equal(first, await repository.FindOldestUnprotectedAsync(CancellationToken.None));

        // 保護すると候補から外れる。
        Assert.True(await repository.SetProtectedAsync(first, isProtected: true, CancellationToken.None));
        Assert.Equal(second, await repository.FindOldestUnprotectedAsync(CancellationToken.None));

        Assert.True(await repository.SetProtectedAsync(second, isProtected: true, CancellationToken.None));
        Assert.Null(await repository.FindOldestUnprotectedAsync(CancellationToken.None));
    }

    private static async Task<(PostgresRecordedRepository Repository, long Id)> CreateRecordedAsync(
        PostgresTestDatabase database)
    {
        PostgresRecordedRepository repository = new(database.ContextFactory, RecordingTestData.Clock());
        long id = await AddRecordedAsync(repository, "録画済み番組");
        return (repository, id);
    }

    private static ValueTask<long> AddRecordedAsync(
        PostgresRecordedRepository repository,
        string name,
        string? description = null,
        string? extended = null,
        int? genre = null,
        long? ruleId = null,
        int startOffsetHours = 0) =>
        repository.AddAsync(
            new CreateRecordedCommand(
                RecordingTestData.ChannelId,
                RecordingTestData.Now.AddHours(startOffsetHours).ToUnixTimeMilliseconds(),
                RecordingTestData.Now.AddHours(startOffsetHours + 1).ToUnixTimeMilliseconds(),
                name,
                RuleId: ruleId,
                Description: description,
                Extended: extended,
                Genre1: genre),
            CancellationToken.None);
}
