using System.Text;
using Microsoft.Extensions.Options;
using TNLAStation.Domain;
using TNLAStation.Infrastructure.Configuration;

namespace TNLAStation.Infrastructure.Mirakurun;

public sealed class MirakurunEpgMapper(IOptions<EpgOptions> options)
{
    private readonly EpgOptions epgOptions = options.Value;
    private static TimeZoneInfo JapanTimeZone { get; } = CreateJapanTimeZone();

    public IReadOnlyList<EpgChannel> MapChannels(IReadOnlyList<MirakurunServiceDto> services)
    {
        var channels = new Dictionary<long, EpgChannel>();
        HashSet<long> excludedChannels = epgOptions.ExcludeChannels.ToHashSet();
        HashSet<int> excludedSids = epgOptions.ExcludeSids.ToHashSet();
        foreach (MirakurunServiceDto service in services)
        {
            if (excludedChannels.Contains(service.Id) || excludedSids.Contains(service.ServiceId))
            {
                continue;
            }

            MirakurunChannelDto sourceChannel = service.Channel ?? throw new InvalidDataException(
                $"Mirakurun service {service.Id} does not contain channel information.");
            int channelTypeId = sourceChannel.Type switch
            {
                "GR" => 0,
                "BS" => 1,
                "CS" => 2,
                "SKY" => 3,
                _ => throw new InvalidDataException($"Unsupported Mirakurun channel type: {sourceChannel.Type}")
            };
            string name = EpgStringNormalizer.RemoveDatabaseUnsupportedCharacters(service.Name);
            channels[service.Id] = new EpgChannel(
                service.Id,
                service.ServiceId,
                service.NetworkId,
                name,
                EpgStringNormalizer.ToHalfWidth(name),
                service.RemoteControlKeyId,
                service.HasLogoData == true,
                channelTypeId,
                sourceChannel.Type,
                sourceChannel.Channel,
                service.Type);
        }

        return channels.Values.ToArray();
    }

    public IReadOnlyList<EpgProgram> MapPrograms(
        IReadOnlyList<MirakurunProgramDto> programs,
        IReadOnlyDictionary<(int NetworkId, int ServiceId), EpgChannel> channelIndex,
        DateTimeOffset updateTime)
    {
        var result = new Dictionary<long, EpgProgram>();
        foreach (MirakurunProgramDto program in programs)
        {
            EpgProgram? mapped = MapProgram(program, channelIndex, updateTime);
            if (mapped is not null)
            {
                result[mapped.Id] = mapped;
            }
        }

        return result.Values.ToArray();
    }

    public EpgProgram? MapProgram(
        MirakurunProgramDto program,
        IReadOnlyDictionary<(int NetworkId, int ServiceId), EpgChannel> channelIndex,
        DateTimeOffset updateTime)
    {
        if (program.Name is null || !IsMainProgram(program) ||
            !channelIndex.TryGetValue((program.NetworkId, program.ServiceId), out EpgChannel? channel))
        {
            return null;
        }

        DateTimeOffset startAt = DateTimeOffset.FromUnixTimeMilliseconds(program.StartAt);
        DateTimeOffset endAt = startAt.AddMilliseconds(program.Duration);
        DateTimeOffset japanStartAt = TimeZoneInfo.ConvertTime(startAt, JapanTimeZone);
        string name = NormalizeDisplayString(program.Name);
        string halfWidthName = EpgStringNormalizer.ToHalfWidth(name);

        string? description = string.IsNullOrEmpty(program.Description)
            ? null
            : NormalizeDisplayString(program.Description);
        string? halfWidthDescription = description is null
            ? null
            : EpgStringNormalizer.ToHalfWidth(description);

        string? extended = CreateExtendedString(program.Extended);
        string? halfWidthExtended = extended is null ? null : EpgStringNormalizer.ToHalfWidth(extended);
        IReadOnlyDictionary<string, string>? rawExtended = program.Extended is null
            ? null
            : new Dictionary<string, string>(program.Extended, StringComparer.Ordinal);
        IReadOnlyDictionary<string, string>? rawHalfWidthExtended = program.Extended is null
            ? null
            : program.Extended.ToDictionary(
                item => EpgStringNormalizer.ToHalfWidth(item.Key),
                item => EpgStringNormalizer.ToHalfWidth(item.Value),
                StringComparer.Ordinal);

        int? genre1 = null;
        int? subGenre1 = null;
        int? genre2 = null;
        int? subGenre2 = null;
        int? genre3 = null;
        int? subGenre3 = null;
        if (program.Genres is { Count: > 0 })
        {
            SetGenre(program.Genres, 0, out genre1, out subGenre1);
            SetGenre(program.Genres, 1, out genre2, out subGenre2);
            SetGenre(program.Genres, 2, out genre3, out subGenre3);
        }

        int? audioSamplingRate = program.Audio?.SamplingRate;
        int? audioComponentType = program.Audio?.ComponentType;
        if (program.Audios is not null)
        {
            foreach (MirakurunAudioDto audio in program.Audios)
            {
                if (!audio.IsMain)
                {
                    continue;
                }

                audioSamplingRate = audio.SamplingRate;
                audioComponentType = audio.ComponentType;
            }
        }

        return new EpgProgram(
            program.Id,
            updateTime,
            channel.Id,
            program.EventId,
            program.ServiceId,
            program.NetworkId,
            startAt,
            endAt,
            japanStartAt.Hour,
            (int)japanStartAt.DayOfWeek,
            program.Duration,
            program.IsFree,
            name,
            halfWidthName,
            EpgStringNormalizer.DeleteBrackets(halfWidthName),
            channel.ChannelType,
            channel.Channel,
            description,
            halfWidthDescription,
            extended,
            halfWidthExtended,
            rawExtended,
            rawHalfWidthExtended,
            genre1,
            subGenre1,
            genre2,
            subGenre2,
            genre3,
            subGenre3,
            program.Video?.Type,
            program.Video?.Resolution,
            program.Video?.StreamContent,
            program.Video?.ComponentType,
            audioSamplingRate,
            audioComponentType);
    }

    public static bool IsMainProgram(MirakurunProgramDto program)
    {
        if (program.RelatedItems is null)
        {
            return true;
        }

        bool onlyRelayItems = true;
        foreach (MirakurunRelatedItemDto item in program.RelatedItems)
        {
            if (item.Type is null || string.Equals(item.Type, "movement", StringComparison.Ordinal))
            {
                return true;
            }

            if (string.Equals(item.Type, "relay", StringComparison.Ordinal))
            {
                continue;
            }

            onlyRelayItems = false;
            if (item.EventId == program.EventId && item.ServiceId == program.ServiceId)
            {
                return true;
            }
        }

        return onlyRelayItems;
    }

    public static IReadOnlyDictionary<(int NetworkId, int ServiceId), EpgChannel> CreateChannelIndex(
        IEnumerable<EpgChannel> channels) =>
        channels.ToDictionary(channel => (channel.NetworkId, channel.ServiceId));

    private string NormalizeDisplayString(string value)
    {
        if (epgOptions.NeedToReplaceEnclosingCharacters)
        {
            value = EpgStringNormalizer.ReplaceEnclosedCharacters(value);
        }

        return EpgStringNormalizer.RemoveDatabaseUnsupportedCharacters(value);
    }

    private string? CreateExtendedString(IReadOnlyDictionary<string, string>? source)
    {
        if (source is null)
        {
            return null;
        }

        var builder = new StringBuilder();
        foreach ((string key, string value) in source)
        {
            builder.Append('\n');
            if (!key.StartsWith('◇'))
            {
                builder.Append('◇');
            }

            builder.Append(key).Append('\n').Append(value);
        }

        string result = EpgStringNormalizer.RemoveDatabaseUnsupportedCharacters(builder.ToString()).Trim();
        return epgOptions.NeedToReplaceEnclosingCharacters
            ? EpgStringNormalizer.ReplaceEnclosedCharacters(result)
            : result;
    }

    private static void SetGenre(
        IReadOnlyList<MirakurunGenreDto> genres,
        int index,
        out int? genre,
        out int? subGenre)
    {
        genre = null;
        subGenre = null;
        if (index >= genres.Count || genres[index].Lv1 >= 0x0e)
        {
            return;
        }

        genre = genres[index].Lv1;
        subGenre = genres[index].Lv2;
    }

    private static TimeZoneInfo CreateJapanTimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Asia/Tokyo");
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.CreateCustomTimeZone("Asia/Tokyo", TimeSpan.FromHours(9), "JST", "JST");
        }
    }
}
