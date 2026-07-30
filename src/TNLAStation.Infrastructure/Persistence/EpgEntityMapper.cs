using System.Text.Json;
using TNLAStation.Domain;

namespace TNLAStation.Infrastructure.Persistence;

internal static class EpgEntityMapper
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = false
    };

    public static EpgChannel ToDomain(this EpgChannelEntity entity) =>
        new(
            entity.Id,
            entity.ServiceId,
            entity.NetworkId,
            entity.Name,
            entity.HalfWidthName,
            entity.RemoteControlKeyId,
            entity.HasLogoData,
            entity.ChannelTypeId,
            entity.ChannelType,
            entity.Channel,
            entity.ServiceType);

    public static EpgProgram ToDomain(this EpgProgramEntity entity) =>
        new(
            entity.Id,
            entity.UpdateTime,
            entity.ChannelId,
            entity.EventId,
            entity.ServiceId,
            entity.NetworkId,
            entity.StartAt,
            entity.EndAt,
            entity.StartHour,
            entity.Week,
            entity.DurationMilliseconds,
            entity.IsFree,
            entity.Name,
            entity.HalfWidthName,
            entity.ShortName,
            entity.ChannelType,
            entity.Channel,
            entity.Description,
            entity.HalfWidthDescription,
            entity.Extended,
            entity.HalfWidthExtended,
            DeserializeDictionary(entity.RawExtendedJson),
            DeserializeDictionary(entity.RawHalfWidthExtendedJson),
            entity.Genre1,
            entity.SubGenre1,
            entity.Genre2,
            entity.SubGenre2,
            entity.Genre3,
            entity.SubGenre3,
            entity.VideoType,
            entity.VideoResolution,
            entity.VideoStreamContent,
            entity.VideoComponentType,
            entity.AudioSamplingRate,
            entity.AudioComponentType,
            DeserializeLongArray(entity.RelayProgramIdsJson));

    public static EpgChannelEntity CreateEntity(EpgChannel channel, DateTimeOffset updatedAt)
    {
        var entity = new EpgChannelEntity
        {
            Name = channel.Name,
            HalfWidthName = channel.HalfWidthName,
            ChannelType = channel.ChannelType,
            Channel = channel.Channel
        };
        UpdateEntity(entity, channel, updatedAt);
        return entity;
    }

    public static void UpdateEntity(EpgChannelEntity entity, EpgChannel channel, DateTimeOffset updatedAt)
    {
        entity.Id = channel.Id;
        entity.ServiceId = channel.ServiceId;
        entity.NetworkId = channel.NetworkId;
        entity.Name = channel.Name;
        entity.HalfWidthName = channel.HalfWidthName;
        entity.RemoteControlKeyId = channel.RemoteControlKeyId;
        entity.HasLogoData = channel.HasLogoData;
        entity.ChannelTypeId = channel.ChannelTypeId;
        entity.ChannelType = channel.ChannelType;
        entity.Channel = channel.Channel;
        entity.ServiceType = channel.ServiceType;
        entity.UpdatedAt = updatedAt;
    }

    public static EpgProgramEntity CreateEntity(EpgProgram program)
    {
        var entity = new EpgProgramEntity
        {
            Name = program.Name,
            HalfWidthName = program.HalfWidthName,
            ShortName = program.ShortName,
            ChannelType = program.ChannelType,
            Channel = program.Channel
        };
        UpdateEntity(entity, program);
        return entity;
    }

    public static void UpdateEntity(EpgProgramEntity entity, EpgProgram program)
    {
        entity.Id = program.Id;
        entity.UpdateTime = program.UpdateTime;
        entity.ChannelId = program.ChannelId;
        entity.EventId = program.EventId;
        entity.ServiceId = program.ServiceId;
        entity.NetworkId = program.NetworkId;
        entity.StartAt = program.StartAt;
        entity.EndAt = program.EndAt;
        entity.StartHour = program.StartHour;
        entity.Week = program.Week;
        entity.DurationMilliseconds = program.DurationMilliseconds;
        entity.IsFree = program.IsFree;
        entity.Name = program.Name;
        entity.HalfWidthName = program.HalfWidthName;
        entity.ShortName = program.ShortName;
        entity.Description = program.Description;
        entity.HalfWidthDescription = program.HalfWidthDescription;
        entity.Extended = program.Extended;
        entity.HalfWidthExtended = program.HalfWidthExtended;
        entity.RawExtendedJson = SerializeDictionary(program.RawExtended);
        entity.RawHalfWidthExtendedJson = SerializeDictionary(program.RawHalfWidthExtended);
        entity.Genre1 = program.Genre1;
        entity.SubGenre1 = program.SubGenre1;
        entity.Genre2 = program.Genre2;
        entity.SubGenre2 = program.SubGenre2;
        entity.Genre3 = program.Genre3;
        entity.SubGenre3 = program.SubGenre3;
        entity.ChannelType = program.ChannelType;
        entity.Channel = program.Channel;
        entity.VideoType = program.VideoType;
        entity.VideoResolution = program.VideoResolution;
        entity.VideoStreamContent = program.VideoStreamContent;
        entity.VideoComponentType = program.VideoComponentType;
        entity.AudioSamplingRate = program.AudioSamplingRate;
        entity.AudioComponentType = program.AudioComponentType;
        entity.RelayProgramIdsJson = program.RelayProgramIds is { Count: > 0 }
            ? JsonSerializer.Serialize(program.RelayProgramIds, JsonOptions)
            : null;
    }

    private static string? SerializeDictionary(IReadOnlyDictionary<string, string>? value) =>
        value is null ? null : JsonSerializer.Serialize(value, JsonOptions);

    private static Dictionary<string, string>? DeserializeDictionary(string? value) =>
        value is null
            ? null
            : JsonSerializer.Deserialize<Dictionary<string, string>>(value, JsonOptions)
                ?? new Dictionary<string, string>(StringComparer.Ordinal);

    private static long[]? DeserializeLongArray(string? value) =>
        value is null ? null : JsonSerializer.Deserialize<long[]>(value, JsonOptions);
}
