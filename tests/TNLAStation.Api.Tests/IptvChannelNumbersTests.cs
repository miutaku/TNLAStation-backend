using TNLAStation.Api.Endpoints;
using TNLAStation.Domain;

namespace TNLAStation.Api.Tests;

public sealed class IptvChannelNumbersTests
{
    private static EpgChannel Channel(
        long id,
        string type,
        int serviceId,
        int? remoteKey = null,
        string name = "局",
        int serviceType = 0x01) =>
        new(id, serviceId, 32736, name, name, remoteKey, false, 0, type, "27", serviceType);

    /// <summary>リモコンの 1〜11 が実機と同じ局を指すよう、各キーの先頭局へその番号を与える。</summary>
    [Fact]
    public void TheFirstServiceOfEachRemoteKeyKeepsThatNumber()
    {
        EpgChannel[] channels =
        [
            Channel(1, "GR", 1024, remoteKey: 1, name: "NHK総合1"),
            Channel(2, "GR", 1032, remoteKey: 2, name: "Eテレ1"),
            Channel(3, "GR", 1040, remoteKey: 4, name: "日テレ1"),
        ];

        IReadOnlyDictionary<long, int> numbers = IptvChannelNumbers.Assign(channels);

        Assert.Equal(1, numbers[1]);
        Assert.Equal(2, numbers[2]);
        Assert.Equal(4, numbers[3]);
    }

    /// <summary>
    /// 同じリモコン番号を共有するサブチャンネルは 21 番以降へ回す。主要局の番号を奪わせない。
    /// </summary>
    [Fact]
    public void SubChannelsMoveOutOfTheRemoteKeyRange()
    {
        EpgChannel[] channels =
        [
            Channel(1, "GR", 1024, remoteKey: 1, name: "NHK総合1"),
            Channel(2, "GR", 1025, remoteKey: 1, name: "NHK総合2"),
            Channel(3, "GR", 1032, remoteKey: 2, name: "Eテレ1"),
            Channel(4, "GR", 1033, remoteKey: 2, name: "Eテレ2"),
        ];

        IReadOnlyDictionary<long, int> numbers = IptvChannelNumbers.Assign(channels);

        Assert.Equal(1, numbers[1]);
        Assert.Equal(2, numbers[3]);
        Assert.Equal([21, 22], new[] { numbers[2], numbers[4] });
    }

    /// <summary>BS は実機と同じ番号。CS は BS と 101/161/800 が重なるので帯を分ける。</summary>
    [Fact]
    public void SatelliteChannelsUseSeparateBands()
    {
        EpgChannel[] channels =
        [
            Channel(1, "BS", 101, name: "NHK BS"),
            Channel(2, "CS", 101, name: "スカパー!インフォ"),
            Channel(3, "BS", 800),
            Channel(4, "CS", 800),
        ];

        IReadOnlyDictionary<long, int> numbers = IptvChannelNumbers.Assign(channels);

        Assert.Equal(101, numbers[1]);
        Assert.Equal(1101, numbers[2]);
        Assert.Equal(800, numbers[3]);
        Assert.Equal(1800, numbers[4]);
        Assert.Equal(4, numbers.Values.Distinct().Count());
    }

    [Fact]
    public void EveryChannelGetsADistinctNumber()
    {
        EpgChannel[] channels =
        [
            .. Enumerable.Range(1, 11).Select(key => Channel(key, "GR", 1000 + key, remoteKey: key)),
            .. Enumerable.Range(0, 20).Select(index => Channel(100 + index, "GR", 2000 + index, remoteKey: 3)),
            .. Enumerable.Range(0, 30).Select(index => Channel(200 + index, "BS", 101 + index)),
            .. Enumerable.Range(0, 30).Select(index => Channel(300 + index, "CS", 55 + index)),
        ];

        IReadOnlyDictionary<long, int> numbers = IptvChannelNumbers.Assign(channels);

        Assert.Equal(channels.Length, numbers.Count);
        Assert.Equal(channels.Length, numbers.Values.Distinct().Count());
    }

    [Theory]
    [InlineData("NHK携帯G・東京", 0x01, true)]
    [InlineData("tvkワンセグ1", 0x01, true)]
    [InlineData("NHK総合1・東京", 0xa5, true)]
    [InlineData("NHK総合1・東京", 0x01, false)]
    public void HandheldServicesAreRecognised(string name, int serviceType, bool expected)
    {
        Assert.Equal(expected, IptvChannelNumbers.IsHandheldService(Channel(1, "GR", 1024, name: name, serviceType: serviceType)));
    }
}
