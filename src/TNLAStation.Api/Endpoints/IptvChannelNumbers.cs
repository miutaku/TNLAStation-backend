using TNLAStation.Domain;

namespace TNLAStation.Api.Endpoints;

/// <summary>
/// IPTV の一覧へ載せるチャンネル番号。番号が無いと、取り込む側が並び順のまま 1 から
/// 通し番号を振り、地上波・BS・CS が一列に並んでリモコンで選べなくなる。
///
/// 割り当て:
///   1〜11  地上波の主要局 (リモコン番号そのもの)
///   21〜   同じリモコン番号を共有するサブチャンネル
///   101〜  BS (service id をそのまま。実機の番号と同じ)
///   1000〜 CS (service id + 1000。BS と 101/161/800 が重なるので帯を分ける)
/// </summary>
internal static class IptvChannelNumbers
{
    private const int SubChannelBase = 21;
    private const int BroadcastSatelliteBase = 0;
    private const int CommunicationSatelliteBase = 1000;

    /// <summary>
    /// TV で見る意味のない低画質サービス。ワンセグ (0xa5/0xa6) と、名前に「携帯」「ワンセグ」を
    /// 含む本編サービス。EPGStation は除いていないので、ここは意図した差。
    /// </summary>
    public static bool IsHandheldService(EpgChannel channel) =>
        channel.ServiceType is 0xa5 or 0xa6 ||
        channel.HalfWidthName.Contains("ワンセグ", StringComparison.Ordinal) ||
        channel.HalfWidthName.Contains("携帯", StringComparison.Ordinal);

    /// <summary>
    /// 並び順のまま番号を決める。同じリモコン番号を持つ局のうち最初のものが主要局。
    /// 呼び出し側が絞り込んだ後の一覧を、そのままの順で渡すこと。
    /// </summary>
    public static IReadOnlyDictionary<long, int> Assign(IEnumerable<EpgChannel> channels)
    {
        var numbers = new Dictionary<long, int>();
        var usedRemoteKeys = new HashSet<int>();
        var subChannels = new List<EpgChannel>();

        foreach (EpgChannel channel in channels)
        {
            switch (channel.ChannelType)
            {
                case "GR" when channel.RemoteControlKeyId is { } key && usedRemoteKeys.Add(key):
                    numbers[channel.Id] = key;
                    break;
                case "GR":
                    subChannels.Add(channel);
                    break;
                case "BS":
                    numbers[channel.Id] = BroadcastSatelliteBase + channel.ServiceId;
                    break;
                default:
                    numbers[channel.Id] = CommunicationSatelliteBase + channel.ServiceId;
                    break;
            }
        }

        int next = SubChannelBase;
        foreach (EpgChannel channel in subChannels)
        {
            numbers[channel.Id] = next++;
        }

        return numbers;
    }
}
