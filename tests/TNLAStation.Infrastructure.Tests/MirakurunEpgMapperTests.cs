using Microsoft.Extensions.Options;
using TNLAStation.Domain;
using TNLAStation.Infrastructure.Configuration;
using TNLAStation.Infrastructure.Mirakurun;

namespace TNLAStation.Infrastructure.Tests;

/// <summary>
/// Mirakurun の DTO からドメインの番組表へ落とす変換。外部仕様の揺れをここで吸収しているため、
/// 分岐ごとに期待値を固定する。
/// </summary>
public sealed class MirakurunEpgMapperTests
{
    private static readonly DateTimeOffset UpdateTime = new(2026, 7, 20, 3, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ChannelTypesMapToTheirStableIds()
    {
        MirakurunEpgMapper mapper = CreateMapper();

        IReadOnlyList<EpgChannel> channels = mapper.MapChannels([
            CreateService(id: 1, serviceId: 1, type: "GR"),
            CreateService(id: 2, serviceId: 2, type: "BS"),
            CreateService(id: 3, serviceId: 3, type: "CS"),
            CreateService(id: 4, serviceId: 4, type: "SKY"),
        ]);

        Assert.Equal([0, 1, 2, 3], channels.Select(channel => channel.ChannelTypeId).ToArray());
        Assert.Equal(["GR", "BS", "CS", "SKY"], channels.Select(channel => channel.ChannelType).ToArray());
    }

    [Fact]
    public void AnUnknownChannelTypeIsRejectedRatherThanGuessed()
    {
        MirakurunEpgMapper mapper = CreateMapper();

        Assert.Throws<InvalidDataException>(() => mapper.MapChannels([CreateService(type: "XX")]));
    }

    [Fact]
    public void ChannelNamesKeepTheOriginalAndGainAHalfWidthCopy()
    {
        MirakurunEpgMapper mapper = CreateMapper();

        EpgChannel channel = Assert.Single(mapper.MapChannels([CreateService(name: "ＮＨＫ総合１・東京")]));

        Assert.Equal("ＮＨＫ総合１・東京", channel.Name);
        Assert.Equal("NHK総合1・東京", channel.HalfWidthName);
    }

    [Fact]
    public void MissingLogoDataIsFalseRatherThanNull()
    {
        MirakurunEpgMapper mapper = CreateMapper();

        EpgChannel channel = Assert.Single(mapper.MapChannels([CreateService(hasLogoData: null)]));

        Assert.False(channel.HasLogoData);
    }

    [Fact]
    public void ExcludedChannelsAndServiceIdsAreDropped()
    {
        MirakurunEpgMapper mapper = CreateMapper(new EpgOptions
        {
            ExcludeChannels = [2],
            ExcludeSids = [3],
        });

        IReadOnlyList<EpgChannel> channels = mapper.MapChannels([
            CreateService(id: 1, serviceId: 1),
            CreateService(id: 2, serviceId: 2),
            CreateService(id: 3, serviceId: 3),
        ]);

        Assert.Equal([1L], channels.Select(channel => channel.Id).ToArray());
    }

    [Fact]
    public void ProgramTimesAreDerivedFromTheJapaneseCalendar()
    {
        MirakurunEpgMapper mapper = CreateMapper();
        var startAt = new DateTimeOffset(2026, 7, 20, 20, 0, 0, TimeSpan.Zero);

        EpgProgram program = MapSingleProgram(mapper, CreateProgram(startAt: startAt, duration: 1_800_000));

        Assert.Equal(5, program.StartHour);
        Assert.Equal((int)DayOfWeek.Tuesday, program.Week);
        Assert.Equal(startAt, program.StartAt);
        Assert.Equal(startAt.AddMinutes(30), program.EndAt);
        Assert.Equal(1_800_000, program.DurationMilliseconds);
    }

    [Fact]
    public void AnUndeterminedDurationUsesTheNextProgramStart()
    {
        MirakurunEpgMapper mapper = CreateMapper();
        DateTimeOffset startAt = UpdateTime.AddMinutes(-5);
        MirakurunProgramDto current = CreateProgram(
            eventId: 1,
            startAt: startAt,
            duration: 1,
            name: "終了時刻未定のニュース");
        MirakurunProgramDto next = CreateProgram(
            eventId: 2,
            startAt: UpdateTime.AddMinutes(30),
            name: "次の番組");
        IReadOnlyDictionary<(int NetworkId, int ServiceId), EpgChannel> index =
            MirakurunEpgMapper.CreateChannelIndex(mapper.MapChannels([CreateService()]));

        EpgProgram mapped = mapper.MapPrograms([current, next], index, UpdateTime)
            .Single(program => program.Id == current.Id);

        Assert.Equal(UpdateTime.AddMinutes(30), mapped.EndAt);
        Assert.Equal((long)TimeSpan.FromMinutes(35).TotalMilliseconds, mapped.DurationMilliseconds);
    }

    [Fact]
    public void AnOngoingUndeterminedDurationRollsPastAStaleNextProgram()
    {
        MirakurunEpgMapper mapper = CreateMapper(new EpgOptions { UpdateIntervalMinutes = 10 });
        MirakurunProgramDto current = CreateProgram(
            eventId: 1,
            startAt: UpdateTime.AddHours(-1),
            duration: 1,
            name: "延長中のニュース");
        MirakurunProgramDto staleNext = CreateProgram(
            eventId: 2,
            startAt: UpdateTime.AddMinutes(-10),
            name: "開始できなかった次番組");
        IReadOnlyDictionary<(int NetworkId, int ServiceId), EpgChannel> index =
            MirakurunEpgMapper.CreateChannelIndex(mapper.MapChannels([CreateService()]));

        EpgProgram mapped = mapper.MapPrograms([current, staleNext], index, UpdateTime)
            .Single(program => program.Id == current.Id);

        Assert.Equal(UpdateTime.AddMinutes(20), mapped.EndAt);
    }

    [Fact]
    public void UpToThreeGenresAreCarriedOverAndTheRestIgnored()
    {
        MirakurunEpgMapper mapper = CreateMapper();

        EpgProgram program = MapSingleProgram(mapper, CreateProgram(genres: [
            new MirakurunGenreDto { Lv1 = 7, Lv2 = 1 },
            new MirakurunGenreDto { Lv1 = 6 },
            new MirakurunGenreDto { Lv1 = 5, Lv2 = 3 },
            new MirakurunGenreDto { Lv1 = 4 },
        ]));

        Assert.Equal(7, program.Genre1);
        Assert.Equal(1, program.SubGenre1);
        Assert.Equal(6, program.Genre2);
        Assert.Null(program.SubGenre2);
        Assert.Equal(5, program.Genre3);
        Assert.Equal(3, program.SubGenre3);
    }

    [Fact]
    public void ExtendedTextIsKeptBothAsRawPairsAndAsFlattenedText()
    {
        MirakurunEpgMapper mapper = CreateMapper();

        EpgProgram program = MapSingleProgram(mapper, CreateProgram(extended: new Dictionary<string, string>
        {
            ["番組内容"] = "本日の特集",
            ["出演者"] = "ＡＢＣ",
        }));

        Assert.NotNull(program.RawExtended);
        Assert.Equal("本日の特集", program.RawExtended!["番組内容"]);
        Assert.Equal("ABC", program.RawHalfWidthExtended!["出演者"]);
        Assert.Contains("本日の特集", program.Extended, StringComparison.Ordinal);
        Assert.Contains("番組内容", program.Extended, StringComparison.Ordinal);
    }

    [Fact]
    public void HalfWidthExtendedMergesCollidingHeadingsWithoutLosingValues()
    {
        MirakurunEpgMapper mapper = CreateMapper();

        EpgProgram program = MapSingleProgram(mapper, CreateProgram(extended: new Dictionary<string, string>
        {
            ["公式ＨＰ"] = "１件目",
            ["番組内容"] = "本日の特集",
            ["公式HP"] = "２件目",
        }));

        Assert.Equal("１件目", program.RawExtended!["公式ＨＰ"]);
        Assert.Equal("２件目", program.RawExtended["公式HP"]);
        Assert.Equal("1件目\n2件目", program.RawHalfWidthExtended!["公式HP"]);
        Assert.Equal(2, program.RawHalfWidthExtended.Count);
    }

    [Fact]
    public void ProgramsWithoutAKnownChannelAreSkipped()
    {
        MirakurunEpgMapper mapper = CreateMapper();
        IReadOnlyDictionary<(int NetworkId, int ServiceId), EpgChannel> index =
            MirakurunEpgMapper.CreateChannelIndex(mapper.MapChannels([CreateService()]));

        EpgProgram? program = mapper.MapProgram(CreateProgram(serviceId: 999), index, UpdateTime);

        Assert.Null(program);
    }

    [Fact]
    public void ProgramsWithoutANameAreSkipped()
    {
        MirakurunEpgMapper mapper = CreateMapper();
        IReadOnlyDictionary<(int NetworkId, int ServiceId), EpgChannel> index =
            MirakurunEpgMapper.CreateChannelIndex(mapper.MapChannels([CreateService()]));

        Assert.Null(mapper.MapProgram(CreateProgram(name: null), index, UpdateTime));
    }

    [Fact]
    public void RelayOnlyEntriesCountAsTheMainProgramButOtherRelationsDoNot()
    {
        Assert.True(MirakurunEpgMapper.IsMainProgram(CreateProgram()));
        Assert.True(MirakurunEpgMapper.IsMainProgram(CreateProgram(relatedItems: [
            new MirakurunRelatedItemDto { Type = "relay", EventId = 99, ServiceId = 1024 },
        ])));
        Assert.False(MirakurunEpgMapper.IsMainProgram(CreateProgram(relatedItems: [
            new MirakurunRelatedItemDto { Type = "shared", EventId = 99, ServiceId = 1024 },
        ])));
        Assert.True(MirakurunEpgMapper.IsMainProgram(CreateProgram(relatedItems: [
            new MirakurunRelatedItemDto { Type = "shared", EventId = 1, ServiceId = 1024 },
        ])));
    }

    [Fact]
    public void RelayRelationsBecomeStableMirakurunProgramIds()
    {
        MirakurunEpgMapper mapper = CreateMapper();
        EpgProgram program = MapSingleProgram(mapper, CreateProgram(relatedItems: [
            new MirakurunRelatedItemDto
            {
                Type = "relay",
                ServiceId = 1025,
                EventId = 23901,
            },
        ]));

        Assert.Equal([327_360_102_523_901L], program.RelayProgramIds);
    }

    [Fact]
    public void EnclosedBroadcastMarksAreSpelledOutOnlyWhenConfigured()
    {
        // U+1F211 は「字」を囲んだ放送マーク。DB や検索で扱いやすい文字列へ開く設定に従う。
        const string name = "ドラマ\U0001f211";

        EpgProgram replaced = MapSingleProgram(CreateMapper(), CreateProgram(name: name));
        EpgProgram kept = MapSingleProgram(
            CreateMapper(new EpgOptions { NeedToReplaceEnclosingCharacters = false }),
            CreateProgram(name: name));

        Assert.Equal("ドラマ[字]", replaced.Name);
        Assert.Equal(name, kept.Name);
    }

    private static EpgProgram MapSingleProgram(MirakurunEpgMapper mapper, MirakurunProgramDto program)
    {
        IReadOnlyDictionary<(int NetworkId, int ServiceId), EpgChannel> index =
            MirakurunEpgMapper.CreateChannelIndex(mapper.MapChannels([CreateService()]));

        return Assert.Single(mapper.MapPrograms([program], index, UpdateTime));
    }

    private static MirakurunEpgMapper CreateMapper(EpgOptions? options = null) =>
        new(Options.Create(options ?? new EpgOptions()));

    private static MirakurunServiceDto CreateService(
        long id = 3_273_601_024,
        int serviceId = 1024,
        int networkId = 32736,
        string name = "ＮＨＫ総合１・東京",
        string type = "GR",
        bool? hasLogoData = true) =>
        new()
        {
            Id = id,
            ServiceId = serviceId,
            NetworkId = networkId,
            Name = name,
            Type = 1,
            HasLogoData = hasLogoData,
            RemoteControlKeyId = 1,
            Channel = new MirakurunChannelDto { Type = type, Channel = "27" },
        };

    private static MirakurunProgramDto CreateProgram(
        long eventId = 1,
        int serviceId = 1024,
        int networkId = 32736,
        string? name = "テスト番組",
        DateTimeOffset? startAt = null,
        long duration = 3_600_000,
        IReadOnlyList<MirakurunGenreDto>? genres = null,
        IReadOnlyDictionary<string, string>? extended = null,
        IReadOnlyList<MirakurunRelatedItemDto>? relatedItems = null) =>
        new()
        {
            Id = 327_360_102_400_000 + eventId,
            EventId = eventId,
            ServiceId = serviceId,
            NetworkId = networkId,
            StartAt = (startAt ?? UpdateTime).ToUnixTimeMilliseconds(),
            Duration = duration,
            IsFree = true,
            Name = name,
            Genres = genres,
            Extended = extended,
            RelatedItems = relatedItems,
        };
}
