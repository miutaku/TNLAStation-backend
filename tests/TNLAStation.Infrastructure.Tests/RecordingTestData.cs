using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using TNLAStation.Domain;
using TNLAStation.Infrastructure.Configuration;
using TNLAStation.Infrastructure.Repositories;

namespace TNLAStation.Infrastructure.Tests;

/// <summary>
/// 予約・録画・エンコードの試験が使う共通の種。番組表を先に置いておかないと、
/// 予約が番組表と結合するところや、番組が消えたときの挙動を確かめられない。
/// </summary>
internal static class RecordingTestData
{
    public static readonly DateTimeOffset Now = new(2026, 7, 20, 12, 0, 0, TimeSpan.Zero);

    public const long ChannelId = 3_273_601_024;

    public static FakeTimeProvider Clock() => new(Now);

    /// <summary>番組表を種として入れる。予約や録画は、この番組を指して作る。</summary>
    public static async Task SeedEpgAsync(PostgresTestDatabase database, params EpgProgram[] programs)
    {
        var repository = new PostgresEpgRepository(
            database.ContextFactory,
            Options.Create(new EpgOptions()),
            Clock());
        await repository.ReplaceSnapshotAsync(
            new EpgSnapshot([CreateChannel()], programs, Now),
            CancellationToken.None);
    }

    public static EpgChannel CreateChannel(long id = ChannelId, int serviceId = 1024) =>
        new(
            id,
            serviceId,
            32736,
            "ＮＨＫ総合１・東京",
            "NHK総合1・東京",
            RemoteControlKeyId: 1,
            HasLogoData: true,
            ChannelTypeId: 0,
            ChannelType: "GR",
            Channel: "27",
            ServiceType: 1);

    public static EpgProgram CreateProgram(
        long id = 1,
        long eventId = 1,
        string name = "テスト番組",
        DateTimeOffset? startAt = null)
    {
        DateTimeOffset start = startAt ?? Now.AddHours(1);
        return new EpgProgram(
            id,
            UpdateTime: Now,
            ChannelId: ChannelId,
            EventId: eventId,
            ServiceId: 1024,
            NetworkId: 32736,
            StartAt: start,
            EndAt: start.AddHours(1),
            StartHour: start.Hour,
            Week: (int)start.DayOfWeek,
            DurationMilliseconds: 3_600_000,
            IsFree: true,
            Name: name,
            HalfWidthName: name,
            ShortName: name,
            ChannelType: "GR",
            Channel: "27",
            Description: "番組の概要",
            HalfWidthDescription: "番組の概要");
    }
}
