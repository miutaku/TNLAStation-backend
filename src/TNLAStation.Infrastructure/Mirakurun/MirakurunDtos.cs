using System.Text.Json;

namespace TNLAStation.Infrastructure.Mirakurun;

public sealed class MirakurunChannelDto
{
    public required string Type { get; init; }

    public required string Channel { get; init; }

    public string? Name { get; init; }
}

public sealed class MirakurunServiceDto
{
    public long Id { get; init; }

    public int ServiceId { get; init; }

    public int NetworkId { get; init; }

    public required string Name { get; init; }

    public int Type { get; init; }

    public int? LogoId { get; init; }

    public bool? HasLogoData { get; init; }

    public int? RemoteControlKeyId { get; init; }

    public bool? EpgReady { get; init; }

    public long? EpgUpdatedAt { get; init; }

    public MirakurunChannelDto? Channel { get; init; }
}

public sealed class MirakurunProgramDto
{
    public long Id { get; init; }

    public long EventId { get; init; }

    public int ServiceId { get; init; }

    public int NetworkId { get; init; }

    public long StartAt { get; init; }

    public long Duration { get; init; }

    public bool IsFree { get; init; }

    public string? Name { get; init; }

    public string? Description { get; init; }

    public IReadOnlyList<MirakurunGenreDto>? Genres { get; init; }

    public MirakurunVideoDto? Video { get; init; }

    public MirakurunAudioDto? Audio { get; init; }

    public IReadOnlyList<MirakurunAudioDto>? Audios { get; init; }

    public IReadOnlyDictionary<string, string>? Extended { get; init; }

    public IReadOnlyList<MirakurunRelatedItemDto>? RelatedItems { get; init; }
}

public sealed class MirakurunGenreDto
{
    public int Lv1 { get; init; }

    public int? Lv2 { get; init; }

    public int? Un1 { get; init; }

    public int? Un2 { get; init; }
}

public sealed class MirakurunVideoDto
{
    public string? Type { get; init; }

    public string? Resolution { get; init; }

    public int? StreamContent { get; init; }

    public int? ComponentType { get; init; }
}

public sealed class MirakurunAudioDto
{
    public int ComponentType { get; init; }

    public int? ComponentTag { get; init; }

    public bool IsMain { get; init; }

    public int SamplingRate { get; init; }

    public IReadOnlyList<string>? Langs { get; init; }
}

public sealed class MirakurunRelatedItemDto
{
    public string? Type { get; init; }

    public int? NetworkId { get; init; }

    public int ServiceId { get; init; }

    public long EventId { get; init; }
}

public sealed class MirakurunEventDto
{
    public required string Resource { get; init; }

    public required string Type { get; init; }

    public JsonElement Data { get; init; }

    public long Time { get; init; }
}

internal sealed class MirakurunRemoveProgramDto
{
    public long Id { get; init; }
}

internal sealed class MirakurunRedefineProgramDto
{
    public long From { get; init; }

    public long To { get; init; }
}
