using TNLAStation.Domain;

namespace TNLAStation.Infrastructure.Persistence;

internal static class RecordedEntityMapper
{
    public static RecordedProgram ToDomain(this RecordedEntity entity, bool isHalfWidth) =>
        new(
            entity.Id,
            entity.ChannelId,
            entity.StartAt.ToUnixTimeMilliseconds(),
            entity.EndAt.ToUnixTimeMilliseconds(),
            entity.Name,
            entity.HalfWidthName,
            IsRecording: entity.IsRecording,
            // エンコードはこれから作る。実行中のものが無いので、常に false で正しい。
            IsEncoding: false,
            IsProtected: entity.IsProtected,
            RuleId: entity.RuleId,
            ProgramId: entity.ProgramId,
            Description: isHalfWidth ? entity.HalfWidthDescription : entity.Description,
            HalfWidthDescription: entity.HalfWidthDescription,
            Extended: isHalfWidth ? entity.HalfWidthExtended : entity.Extended,
            HalfWidthExtended: entity.HalfWidthExtended,
            Genre1: entity.Genre1,
            SubGenre1: entity.SubGenre1,
            Genre2: entity.Genre2,
            SubGenre2: entity.SubGenre2,
            Genre3: entity.Genre3,
            SubGenre3: entity.SubGenre3,
            DropLogFile: entity.DropLogFile is { } drop
                ? new DropLogFile(drop.Id, (int)drop.ErrorCount, (int)drop.DropCount, (int)drop.ScramblingCount)
                : null,
            Thumbnails: [.. entity.Thumbnails.OrderBy(thumbnail => thumbnail.Id).Select(thumbnail => thumbnail.Id)],
            VideoFiles: [.. entity.VideoFiles
                .OrderBy(file => file.Id)
                .Select(file => new VideoFile(file.Id, file.Name, file.Filename, file.Type, file.Size))],
            Tags: [.. entity.TagLinks
                .Where(link => link.Tag is not null)
                .Select(link => new RecordedTag(link.Tag!.Id, link.Tag.Name, link.Tag.Color))
                .OrderBy(tag => tag.Name, StringComparer.Ordinal)]);
}
