namespace TNLAStation.Infrastructure.Persistence;

public sealed class EpgChannelEntity
{
    public long Id { get; set; }

    public int ServiceId { get; set; }

    public int NetworkId { get; set; }

    public required string Name { get; set; }

    public required string HalfWidthName { get; set; }

    public int? RemoteControlKeyId { get; set; }

    public bool HasLogoData { get; set; }

    public int ChannelTypeId { get; set; }

    public required string ChannelType { get; set; }

    public required string Channel { get; set; }

    public int? ServiceType { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public ICollection<EpgProgramEntity> Programs { get; } = new List<EpgProgramEntity>();
}

public sealed class EpgProgramEntity
{
    public long Id { get; set; }

    public DateTimeOffset UpdateTime { get; set; }

    public long ChannelId { get; set; }

    public long EventId { get; set; }

    public int ServiceId { get; set; }

    public int NetworkId { get; set; }

    public DateTimeOffset StartAt { get; set; }

    public DateTimeOffset EndAt { get; set; }

    public int StartHour { get; set; }

    public int Week { get; set; }

    public long DurationMilliseconds { get; set; }

    public bool IsFree { get; set; }

    public required string Name { get; set; }

    public required string HalfWidthName { get; set; }

    public required string ShortName { get; set; }

    public string? Description { get; set; }

    public string? HalfWidthDescription { get; set; }

    public string? Extended { get; set; }

    public string? HalfWidthExtended { get; set; }

    public string? RawExtendedJson { get; set; }

    public string? RawHalfWidthExtendedJson { get; set; }

    public int? Genre1 { get; set; }

    public int? SubGenre1 { get; set; }

    public int? Genre2 { get; set; }

    public int? SubGenre2 { get; set; }

    public int? Genre3 { get; set; }

    public int? SubGenre3 { get; set; }

    public required string ChannelType { get; set; }

    public required string Channel { get; set; }

    public string? VideoType { get; set; }

    public string? VideoResolution { get; set; }

    public int? VideoStreamContent { get; set; }

    public int? VideoComponentType { get; set; }

    public int? AudioSamplingRate { get; set; }

    public int? AudioComponentType { get; set; }

    public EpgChannelEntity? ChannelEntity { get; set; }
}

public sealed class EpgSyncStateEntity
{
    public short SingletonId { get; set; } = 1;

    public long Generation { get; set; }

    public bool NeedsFullSync { get; set; } = true;

    public DateTimeOffset? LastAttemptAt { get; set; }

    public DateTimeOffset? LastSuccessAt { get; set; }

    public DateTimeOffset? LastStreamEventAt { get; set; }

    public string? LastError { get; set; }
}
