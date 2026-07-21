using System.Text.Json;
using TNLAStation.Application.Models;
using TNLAStation.Domain;

namespace TNLAStation.Infrastructure.Persistence;

internal static class RuleEntityMapper
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static RecordingRule ToDomain(this RuleEntity entity) =>
        new(
            entity.Id,
            entity.IsTimeSpecification,
            new EpgSearchQuery(
                entity.Keyword,
                entity.IgnoreKeyword,
                entity.KeyCaseSensitive,
                entity.KeyRegularExpression,
                entity.Name,
                entity.Description,
                entity.Extended,
                entity.IgnoreKeyCaseSensitive,
                entity.IgnoreKeyRegularExpression,
                entity.IgnoreName,
                entity.IgnoreDescription,
                entity.IgnoreExtended,
                entity.Gr,
                entity.Bs,
                entity.Cs,
                entity.Sky,
                Deserialize<long>(entity.ChannelIdsJson),
                Deserialize<EpgSearchGenre>(entity.GenresJson),
                Deserialize<EpgSearchTime>(entity.TimesJson),
                entity.IsFree,
                entity.DurationMin,
                entity.DurationMax,
                Deserialize<EpgSearchPeriod>(entity.SearchPeriodsJson)),
            new RuleReserveOption(
                entity.Enable,
                entity.AllowEndLack,
                entity.AvoidDuplicate,
                entity.PeriodToAvoidDuplicate,
                Deserialize<long>(entity.TagsJson),
                entity.Priority),
            ToSaveSettings(entity),
            ToEncodeSettings(entity),
            entity.UpdateCount);

    public static void Apply(this RuleEntity entity, RecordingRule rule)
    {
        EpgSearchQuery search = rule.SearchOption;
        entity.IsTimeSpecification = rule.IsTimeSpecification;
        entity.Keyword = search.Keyword;
        entity.HalfWidthKeyword = ToHalfWidth(search.Keyword);
        entity.IgnoreKeyword = search.IgnoreKeyword;
        entity.HalfWidthIgnoreKeyword = ToHalfWidth(search.IgnoreKeyword);
        entity.KeyCaseSensitive = search.KeyCaseSensitive;
        entity.KeyRegularExpression = search.KeyRegularExpression;
        entity.Name = search.Name;
        entity.Description = search.Description;
        entity.Extended = search.Extended;
        entity.IgnoreKeyCaseSensitive = search.IgnoreKeyCaseSensitive;
        entity.IgnoreKeyRegularExpression = search.IgnoreKeyRegularExpression;
        entity.IgnoreName = search.IgnoreName;
        entity.IgnoreDescription = search.IgnoreDescription;
        entity.IgnoreExtended = search.IgnoreExtended;
        entity.Gr = search.Gr;
        entity.Bs = search.Bs;
        entity.Cs = search.Cs;
        entity.Sky = search.Sky;
        entity.ChannelIdsJson = Serialize(search.ChannelIds);
        entity.GenresJson = Serialize(search.Genres);
        entity.TimesJson = Serialize(search.Times);
        entity.IsFree = search.IsFree;
        entity.DurationMin = search.DurationMin;
        entity.DurationMax = search.DurationMax;
        entity.SearchPeriodsJson = Serialize(search.SearchPeriods);

        RuleReserveOption reserve = rule.ReserveOption;
        entity.Enable = reserve.Enable;
        entity.AllowEndLack = reserve.AllowEndLack;
        entity.AvoidDuplicate = reserve.AvoidDuplicate;
        entity.PeriodToAvoidDuplicate = reserve.PeriodToAvoidDuplicate;
        entity.TagsJson = Serialize(reserve.Tags);
        entity.Priority = reserve.Priority;

        entity.ParentDirectoryName = rule.SaveOption?.ParentDirectoryName;
        entity.Directory = rule.SaveOption?.Directory;
        entity.RecordedFormat = rule.SaveOption?.RecordedFormat;

        ReserveEncodeSettings? encode = rule.EncodeOption;
        entity.Mode1 = encode?.Mode1;
        entity.ParentDirectoryName1 = encode?.EncodeParentDirectoryName1;
        entity.Directory1 = encode?.Directory1;
        entity.Mode2 = encode?.Mode2;
        entity.ParentDirectoryName2 = encode?.EncodeParentDirectoryName2;
        entity.Directory2 = encode?.Directory2;
        entity.Mode3 = encode?.Mode3;
        entity.ParentDirectoryName3 = encode?.EncodeParentDirectoryName3;
        entity.Directory3 = encode?.Directory3;
        entity.IsDeleteOriginalAfterEncode = encode?.IsDeleteOriginalAfterEncode == true;
    }

    private static ReserveSaveSettings? ToSaveSettings(RuleEntity entity) =>
        entity.ParentDirectoryName is null && entity.Directory is null && entity.RecordedFormat is null
            ? null
            : new ReserveSaveSettings(entity.ParentDirectoryName, entity.Directory, entity.RecordedFormat);

    private static ReserveEncodeSettings? ToEncodeSettings(RuleEntity entity)
    {
        bool hasEncodeOption = entity.Mode1 is not null || entity.ParentDirectoryName1 is not null ||
            entity.Directory1 is not null || entity.Mode2 is not null || entity.ParentDirectoryName2 is not null ||
            entity.Directory2 is not null || entity.Mode3 is not null || entity.ParentDirectoryName3 is not null ||
            entity.Directory3 is not null;

        return hasEncodeOption
            ? new ReserveEncodeSettings(
                entity.Mode1,
                entity.ParentDirectoryName1,
                entity.Directory1,
                entity.Mode2,
                entity.ParentDirectoryName2,
                entity.Directory2,
                entity.Mode3,
                entity.ParentDirectoryName3,
                entity.Directory3,
                entity.IsDeleteOriginalAfterEncode)
            : null;
    }

    private static string? ToHalfWidth(string? value) =>
        value is null ? null : EpgStringNormalizer.ToHalfWidth(value);

    private static string? Serialize<T>(IReadOnlyList<T>? value) =>
        value is null ? null : JsonSerializer.Serialize(value, JsonOptions);

    private static T[]? Deserialize<T>(string? value) =>
        value is null ? null : JsonSerializer.Deserialize<T[]>(value, JsonOptions);
}
