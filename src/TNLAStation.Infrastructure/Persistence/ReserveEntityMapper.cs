using System.Text.Json;
using TNLAStation.Domain;

namespace TNLAStation.Infrastructure.Persistence;

internal static class ReserveEntityMapper
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// 予約 1 件を組み立てる。番組の詳細は番組表から取り、保存や変換の指定は手動予約から取る。
    /// 番組表から消えた番組でも、予約の行が持つ時刻と番組名で一覧に出せる。
    /// </summary>
    public static Reservation ToDomain(
        this ReserveEntity entity,
        EpgProgramEntity? program,
        ManualReserveEntity? manual) =>
        new(
            entity.Id,
            entity.IsSkip,
            entity.IsConflict,
            entity.IsOverlap,
            AllowEndLack: manual?.AllowEndLack ?? true,
            IsTimeSpecified: manual?.IsTimeSpecified ?? false,
            IsDeleteOriginalAfterEncode: manual?.IsDeleteOriginalAfterEncode ?? false,
            entity.ChannelId,
            entity.StartAt.ToUnixTimeMilliseconds(),
            entity.EndAt.ToUnixTimeMilliseconds(),
            program?.Name ?? entity.Name,
            program?.HalfWidthName ?? entity.HalfWidthName,
            RuleId: entity.RuleId,
            Priority: entity.Priority,
            Tags: DeserializeTags(manual?.TagsJson),
            ParentDirectoryName: manual?.ParentDirectoryName,
            Directory: manual?.Directory,
            RecordedFormat: manual?.RecordedFormat,
            EncodeMode1: manual?.Mode1,
            EncodeParentDirectoryName1: manual?.ParentDirectoryName1,
            EncodeDirectory1: manual?.Directory1,
            EncodeMode2: manual?.Mode2,
            EncodeParentDirectoryName2: manual?.ParentDirectoryName2,
            EncodeDirectory2: manual?.Directory2,
            EncodeMode3: manual?.Mode3,
            EncodeParentDirectoryName3: manual?.ParentDirectoryName3,
            EncodeDirectory3: manual?.Directory3,
            ProgramId: entity.ProgramId,
            Description: program?.Description,
            HalfWidthDescription: program?.HalfWidthDescription,
            Extended: program?.Extended,
            HalfWidthExtended: program?.HalfWidthExtended,
            RawExtended: DeserializeDictionary(program?.RawExtendedJson),
            HalfWidthRawExtended: DeserializeDictionary(program?.RawHalfWidthExtendedJson),
            Genre1: program?.Genre1,
            SubGenre1: program?.SubGenre1,
            Genre2: program?.Genre2,
            SubGenre2: program?.SubGenre2,
            Genre3: program?.Genre3,
            SubGenre3: program?.SubGenre3,
            VideoType: program?.VideoType,
            VideoResolution: program?.VideoResolution,
            VideoStreamContent: program?.VideoStreamContent,
            VideoComponentType: program?.VideoComponentType,
            AudioSamplingRate: program?.AudioSamplingRate,
            AudioComponentType: program?.AudioComponentType);

    private static long[]? DeserializeTags(string? json) =>
        string.IsNullOrWhiteSpace(json) ? null : JsonSerializer.Deserialize<long[]>(json, JsonOptions);

    private static Dictionary<string, string>? DeserializeDictionary(string? json) =>
        string.IsNullOrWhiteSpace(json)
            ? null
            : JsonSerializer.Deserialize<Dictionary<string, string>>(json, JsonOptions);
}
