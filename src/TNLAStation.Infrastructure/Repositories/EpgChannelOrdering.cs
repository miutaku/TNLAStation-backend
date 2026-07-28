using TNLAStation.Domain;
using TNLAStation.Infrastructure.Configuration;

namespace TNLAStation.Infrastructure.Repositories;

internal static class EpgChannelOrdering
{
    public static IReadOnlyList<EpgChannel> Apply(IEnumerable<EpgChannel> source, EpgOptions options)
    {
        List<EpgChannel> channels = source
            .OrderBy(channel => channel.ChannelTypeId)
            .ThenBy(channel => channel.RemoteControlKeyId is null ? 0 : 1)
            .ThenBy(channel => channel.RemoteControlKeyId)
            .ThenBy(channel => channel.ServiceId)
            .ToList();

        IReadOnlyList<long>? order = options.ChannelOrder.Count > 0 ? options.ChannelOrder : null;
        if (order is not null)
        {
            MoveToFront(channels, order, channel => channel.Id);
        }
        else if (options.SidOrder.Count > 0)
        {
            MoveToFront(channels, options.SidOrder, channel => channel.ServiceId);
        }

        return channels;
    }

    private static void MoveToFront<T>(List<EpgChannel> channels, IReadOnlyList<T> order, Func<EpgChannel, T> key)
        where T : notnull
    {
        int destination = 0;
        foreach (T expected in order)
        {
            int current = channels.FindIndex(channel => EqualityComparer<T>.Default.Equals(key(channel), expected));
            if (current < 0)
            {
                continue;
            }

            EpgChannel channel = channels[current];
            channels.RemoveAt(current);
            channels.Insert(destination, channel);
            destination++;
        }
    }
}
