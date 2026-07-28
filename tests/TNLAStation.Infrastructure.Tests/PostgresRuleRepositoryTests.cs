using TNLAStation.Application.Models;
using TNLAStation.Infrastructure.Repositories;

namespace TNLAStation.Infrastructure.Tests;

/// <summary>
/// ルール編集は行を部分更新せず、検索・予約・保存・エンコード設定を一式で
/// 差し替える。JSON列を含む全項目が PostgreSQL の往復で欠落しないことを確認する。
/// </summary>
public sealed class PostgresRuleRepositoryTests
{
    [PostgresFact]
    public async Task UpdateRoundTripsEveryRuleOption()
    {
        await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
        var repository = new PostgresRuleRepository(database.ContextFactory);
        long id = await repository.AddAsync(CreateRule(), CancellationToken.None);

        RecordingRule updated = CreateRule() with
        {
            Id = id,
            Name = "  深夜アニメ・更新版  ",
            IsTimeSpecification = true,
            SearchOption = new EpgSearchQuery(
                Keyword: "新作アニメ",
                IgnoreKeyword: "再放送",
                KeyCaseSensitive: true,
                KeyRegularExpression: true,
                Name: true,
                Description: false,
                Extended: true,
                IgnoreKeyCaseSensitive: true,
                IgnoreKeyRegularExpression: true,
                IgnoreName: true,
                IgnoreDescription: false,
                IgnoreExtended: true,
                Gr: true,
                Bs: true,
                Cs: false,
                Sky: false,
                ChannelIds: [11, 12],
                Genres: [new EpgSearchGenre(7, 1), new EpgSearchGenre(7, 2)],
                Times: [new EpgSearchTime(2, 23, 2), new EpgSearchTime(4, 1, 1)],
                IsFree: true,
                DurationMin: 1_800,
                DurationMax: 7_200,
                SearchPeriods:
                [
                    new EpgSearchPeriod(
                        new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.FromHours(9)),
                        new DateTimeOffset(2027, 2, 1, 0, 0, 0, TimeSpan.FromHours(9))),
                    new EpgSearchPeriod(
                        new DateTimeOffset(2028, 1, 1, 0, 0, 0, TimeSpan.FromHours(9)),
                        new DateTimeOffset(2028, 2, 1, 0, 0, 0, TimeSpan.FromHours(9))),
                ]),
            ReserveOption = new RuleReserveOption(
                Enable: false,
                AllowEndLack: false,
                AvoidDuplicate: true,
                PeriodToAvoidDuplicate: 12,
                Tags: [4, 9],
                Priority: 1),
            SaveOption = new ReserveSaveSettings("recorded", "anime/late-night", "%TITLE%"),
            EncodeOption = new ReserveEncodeSettings(
                "H.265",
                "encoded",
                "anime",
                "H.264",
                "mobile",
                "anime",
                null,
                null,
                null,
                IsDeleteOriginalAfterEncode: true),
        };

        await repository.UpdateAsync(updated, CancellationToken.None);

        RecordingRule actual = Assert.IsType<RecordingRule>(
            await repository.GetAsync(id, CancellationToken.None));
        Assert.Equal("深夜アニメ・更新版", actual.Name);
        Assert.True(actual.IsTimeSpecification);
        Assert.Equal(updated.SearchOption.Keyword, actual.SearchOption.Keyword);
        Assert.Equal(updated.SearchOption.IgnoreKeyword, actual.SearchOption.IgnoreKeyword);
        Assert.True(actual.SearchOption.KeyCaseSensitive);
        Assert.True(actual.SearchOption.KeyRegularExpression);
        Assert.True(actual.SearchOption.IgnoreKeyCaseSensitive);
        Assert.True(actual.SearchOption.IgnoreKeyRegularExpression);
        Assert.Equal(updated.SearchOption.ChannelIds, actual.SearchOption.ChannelIds);
        Assert.Equal(updated.SearchOption.Genres, actual.SearchOption.Genres);
        Assert.Equal(updated.SearchOption.Times, actual.SearchOption.Times);
        Assert.Equal(updated.SearchOption.SearchPeriods, actual.SearchOption.SearchPeriods);
        Assert.Equal(1_800, actual.SearchOption.DurationMin);
        Assert.Equal(7_200, actual.SearchOption.DurationMax);
        Assert.Equal(updated.ReserveOption.Enable, actual.ReserveOption.Enable);
        Assert.Equal(updated.ReserveOption.AllowEndLack, actual.ReserveOption.AllowEndLack);
        Assert.Equal(updated.ReserveOption.AvoidDuplicate, actual.ReserveOption.AvoidDuplicate);
        Assert.Equal(updated.ReserveOption.PeriodToAvoidDuplicate, actual.ReserveOption.PeriodToAvoidDuplicate);
        Assert.Equal(updated.ReserveOption.Tags, actual.ReserveOption.Tags);
        Assert.Equal(updated.ReserveOption.Priority, actual.ReserveOption.Priority);
        Assert.Equal(updated.SaveOption, actual.SaveOption);
        Assert.Equal(updated.EncodeOption, actual.EncodeOption);
        Assert.Equal(1, actual.UpdateCount);
    }

    private static RecordingRule CreateRule() =>
        new(
            Id: 0,
            IsTimeSpecification: false,
            SearchOption: new EpgSearchQuery(Keyword: "アニメ", Name: true, Gr: true),
            ReserveOption: new RuleReserveOption(
                Enable: true,
                AllowEndLack: true,
                AvoidDuplicate: false),
            Name: "深夜アニメ");
}
