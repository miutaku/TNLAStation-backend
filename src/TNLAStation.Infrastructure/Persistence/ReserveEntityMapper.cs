using System.Text.Json;
using TNLAStation.Application.Models;
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
        ManualReserveEntity? manual,
        CreateReserveCommand? edit,
        RecordingRule? rule = null) =>
        new(
            entity.Id,
            entity.IsSkip,
            entity.IsConflict,
            entity.IsOverlap,
            AllowEndLack: edit?.AllowEndLack ?? manual?.AllowEndLack ??
                rule?.ReserveOption.AllowEndLack ?? true,
            IsTimeSpecified: manual?.IsTimeSpecified ?? false,
            IsDeleteOriginalAfterEncode: edit?.Encode?.IsDeleteOriginalAfterEncode ??
                manual?.IsDeleteOriginalAfterEncode ??
                rule?.EncodeOption?.IsDeleteOriginalAfterEncode ?? false,
            entity.ChannelId,
            entity.StartAt.ToUnixTimeMilliseconds(),
            entity.EndAt.ToUnixTimeMilliseconds(),
            program?.Name ?? entity.Name,
            program?.HalfWidthName ?? entity.HalfWidthName,
            RuleId: entity.RuleId,
            Priority: entity.Priority,
            Tags: edit?.Tags ?? DeserializeTags(manual?.TagsJson) ?? rule?.ReserveOption.Tags,
            ParentDirectoryName: edit?.Save?.ParentDirectoryName ?? manual?.ParentDirectoryName ??
                rule?.SaveOption?.ParentDirectoryName,
            Directory: edit?.Save?.Directory ?? manual?.Directory ?? rule?.SaveOption?.Directory,
            RecordedFormat: edit?.Save?.RecordedFormat ?? manual?.RecordedFormat ?? rule?.SaveOption?.RecordedFormat,
            EncodeMode1: edit?.Encode?.Mode1 ?? manual?.Mode1 ?? rule?.EncodeOption?.Mode1,
            EncodeParentDirectoryName1: edit?.Encode?.EncodeParentDirectoryName1 ?? manual?.ParentDirectoryName1 ??
                rule?.EncodeOption?.EncodeParentDirectoryName1,
            EncodeDirectory1: edit?.Encode?.Directory1 ?? manual?.Directory1 ?? rule?.EncodeOption?.Directory1,
            EncodeMode2: edit?.Encode?.Mode2 ?? manual?.Mode2 ?? rule?.EncodeOption?.Mode2,
            EncodeParentDirectoryName2: edit?.Encode?.EncodeParentDirectoryName2 ?? manual?.ParentDirectoryName2 ??
                rule?.EncodeOption?.EncodeParentDirectoryName2,
            EncodeDirectory2: edit?.Encode?.Directory2 ?? manual?.Directory2 ?? rule?.EncodeOption?.Directory2,
            EncodeMode3: edit?.Encode?.Mode3 ?? manual?.Mode3 ?? rule?.EncodeOption?.Mode3,
            EncodeParentDirectoryName3: edit?.Encode?.EncodeParentDirectoryName3 ?? manual?.ParentDirectoryName3 ??
                rule?.EncodeOption?.EncodeParentDirectoryName3,
            EncodeDirectory3: edit?.Encode?.Directory3 ?? manual?.Directory3 ?? rule?.EncodeOption?.Directory3,
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
            AudioComponentType: program?.AudioComponentType,
            ReserveKey: entity.Key,
            ManualReserveId: entity.ManualReserveId,
            RuleName: rule is null ? null : NormalizeRuleName(rule.Name));

    private static long[]? DeserializeTags(string? json) =>
        string.IsNullOrWhiteSpace(json) ? null : JsonSerializer.Deserialize<long[]>(json, JsonOptions);

    private static string NormalizeRuleName(string? displayName) =>
        !string.IsNullOrWhiteSpace(displayName)
            ? displayName.Trim()
            : "無題のルール";

    private static Dictionary<string, string>? DeserializeDictionary(string? json) =>
        string.IsNullOrWhiteSpace(json)
            ? null
            : JsonSerializer.Deserialize<Dictionary<string, string>>(json, JsonOptions);
}
