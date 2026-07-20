using TNLAStation.Application.Models;
using TNLAStation.Domain;

namespace TNLAStation.Api.Contracts;

internal static class EpgContractMapper
{
    public static ChannelItemResponse ToChannelResponse(this EpgChannel channel) =>
        new(
            channel.Id,
            channel.ServiceId,
            channel.NetworkId,
            channel.Name,
            channel.HalfWidthName,
            channel.HasLogoData,
            channel.ChannelType,
            channel.Channel)
        {
            RemoteControlKeyId = channel.RemoteControlKeyId,
            Type = channel.ServiceType
        };

    public static ScheduleChannelResponse ToScheduleChannelResponse(this EpgChannel channel, bool isHalfWidth) =>
        new(
            channel.Id,
            channel.ServiceId,
            channel.NetworkId,
            isHalfWidth ? channel.HalfWidthName : channel.Name,
            channel.HasLogoData,
            channel.ChannelType)
        {
            RemoteControlKeyId = channel.RemoteControlKeyId,
            Type = channel.ServiceType
        };

    public static ScheduleProgramResponse ToScheduleProgramResponse(
        this EpgProgram program,
        bool isHalfWidth,
        bool needsRawExtended) =>
        new(
            program.Id,
            program.ChannelId,
            program.StartAt.ToUnixTimeMilliseconds(),
            program.EndAt.ToUnixTimeMilliseconds(),
            program.IsFree,
            isHalfWidth ? program.HalfWidthName : program.Name)
        {
            Description = isHalfWidth ? program.HalfWidthDescription : program.Description,
            Extended = isHalfWidth ? program.HalfWidthExtended : program.Extended,
            RawExtended = needsRawExtended
                ? isHalfWidth ? program.RawHalfWidthExtended : program.RawExtended
                : null,
            Genre1 = program.Genre1,
            SubGenre1 = program.SubGenre1,
            Genre2 = program.Genre2,
            SubGenre2 = program.SubGenre2,
            Genre3 = program.Genre3,
            SubGenre3 = program.SubGenre3,
            VideoType = program.VideoType,
            VideoResolution = program.VideoResolution,
            VideoStreamContent = program.VideoStreamContent,
            VideoComponentType = program.VideoComponentType,
            AudioSamplingRate = program.AudioSamplingRate,
            AudioComponentType = program.AudioComponentType
        };

    public static EpgSearchQuery ToQuery(this ScheduleSearchRequest request)
    {
        RuleSearchRequest option = request.Option;
        return new EpgSearchQuery(
            option.Keyword,
            option.IgnoreKeyword,
            option.KeyCS == true,
            option.KeyRegExp == true,
            option.Name == true,
            option.Description == true,
            option.Extended == true,
            option.IgnoreKeyCS == true,
            option.IgnoreKeyRegExp == true,
            option.IgnoreName == true,
            option.IgnoreDescription == true,
            option.IgnoreExtended == true,
            option.Gr == true,
            option.Bs == true,
            option.Cs == true,
            option.Sky == true,
            option.ChannelIds,
            option.Genres?.Select(genre => new EpgSearchGenre(genre.Genre, genre.SubGenre)).ToArray(),
            option.Times?.Select(time => new EpgSearchTime(time.Week, time.Start, time.Range)).ToArray(),
            option.IsFree == true,
            option.DurationMin,
            option.DurationMax,
            option.SearchPeriods?.Select(period => new EpgSearchPeriod(
                DateTimeOffset.FromUnixTimeMilliseconds(period.StartAt),
                DateTimeOffset.FromUnixTimeMilliseconds(period.EndAt))).ToArray(),
            request.Limit);
    }
}
