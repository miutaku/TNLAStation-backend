using TNLAStation.Domain;

namespace TNLAStation.Api.Contracts;

internal static class ContractMapper
{
    public static ConfigResponse ToResponse(this StationConfiguration config) =>
        new(
            SocketIOPort: config.SocketIoPort,
            Broadcast: new BroadcastResponse(
                config.Broadcast.Gr,
                config.Broadcast.Bs,
                config.Broadcast.Cs,
                config.Broadcast.Sky),
            Recorded: config.RecordedDirectories,
            Encode: config.EncodeModes,
            Urlscheme: new UrlSchemeResponse(
                config.UrlScheme.M2Ts.ToResponse(),
                config.UrlScheme.Video.ToResponse(),
                config.UrlScheme.Download.ToResponse()),
            IsEnableTSLiveStream: config.IsEnableTsLiveStream,
            IsEnableTSRecordedStream: config.IsEnableTsRecordedStream,
            IsEnableEncodedRecordedStream: config.IsEnableEncodedRecordedStream)
        {
            KodiHosts = config.KodiHosts,
            StreamConfig = config.StreamConfig?.ToResponse(),
        };

    private static StreamConfigurationResponse ToResponse(this StreamConfiguration config) => new()
    {
        Live = config.Live is null ? null : new LiveStreamConfigurationResponse { Ts = config.Live.Ts?.ToResponse() },
        Recorded = config.Recorded is null
            ? null
            : new RecordedStreamConfigurationResponse
            {
                Ts = config.Recorded.Ts?.ToResponse(),
                Encoded = config.Recorded.Encoded?.ToResponse(),
            },
    };

    private static TransportStreamConfigurationResponse ToResponse(this TransportStreamConfiguration config) => new()
    {
        M2Ts = config.M2Ts?.Select(entry => new M2TsStreamParameterResponse(entry.Name, entry.IsUnconverted)).ToArray(),
        M2TsLl = config.M2TsLl,
        Webm = config.Webm,
        Mp4 = config.Mp4,
        Hls = config.Hls,
    };

    private static RecordedStreamModesResponse ToResponse(this RecordedStreamModes config) => new()
    {
        Webm = config.Webm,
        Mp4 = config.Mp4,
        Hls = config.Hls,
    };

    public static RecordedItemResponse ToResponse(this RecordedProgram item, bool isHalfWidth) =>
        new(
            Id: item.Id,
            ChannelId: item.ChannelId,
            StartAt: item.StartAt,
            EndAt: item.EndAt,
            Name: isHalfWidth ? item.HalfWidthName : item.Name,
            IsRecording: item.IsRecording,
            IsEncoding: item.IsEncoding,
            IsProtected: item.IsProtected)
        {
            RuleId = item.RuleId,
            ProgramId = item.ProgramId,
            Description = isHalfWidth ? item.HalfWidthDescription : item.Description,
            Extended = isHalfWidth ? item.HalfWidthExtended : item.Extended,
            // EPGStation 2.10.0 accidentally emits rawExtended only for half-width requests.
            // When half-width data is absent it falls back to the original rawExtended value.
            RawExtended = isHalfWidth ? item.HalfWidthRawExtended ?? item.RawExtended : null,
            Genre1 = item.Genre1,
            SubGenre1 = item.SubGenre1,
            Genre2 = item.Genre2,
            SubGenre2 = item.SubGenre2,
            Genre3 = item.Genre3,
            SubGenre3 = item.SubGenre3,
            VideoType = item.VideoType,
            VideoResolution = item.VideoResolution,
            VideoStreamContent = item.VideoStreamContent,
            VideoComponentType = item.VideoComponentType,
            AudioSamplingRate = item.AudioSamplingRate,
            AudioComponentType = item.AudioComponentType,
            Thumbnails = item.Thumbnails,
            VideoFiles = item.VideoFiles?.Select(file => new VideoFileResponse(
                file.Id,
                file.Name,
                file.Filename,
                file.Type,
                file.Size)).ToArray(),
            DropLogFile = item.DropLogFile is null
                ? null
                : new DropLogFileResponse(
                    item.DropLogFile.Id,
                    item.DropLogFile.ErrorCount,
                    item.DropLogFile.DropCount,
                    item.DropLogFile.ScramblingCount),
            // EPGStation omits tags when no relation exists; it does not emit an empty array.
            Tags = item.Tags is { Count: > 0 }
                ? item.Tags.Select(tag => new RecordedTagResponse(tag.Id, tag.Name, tag.Color)).ToArray()
                : null
        };

    public static ReserveListItemResponse ToListItemResponse(this Reservation item) =>
        new(item.Id) { ProgramId = item.ProgramId, RuleId = item.RuleId };

    public static ReserveItemResponse ToResponse(this Reservation item, bool isHalfWidth) =>
        new(
            Id: item.Id,
            IsSkip: item.IsSkip,
            IsConflict: item.IsConflict,
            IsOverlap: item.IsOverlap,
            AllowEndLack: item.AllowEndLack,
            IsTimeSpecified: item.IsTimeSpecified,
            IsDeleteOriginalAfterEncode: item.IsDeleteOriginalAfterEncode,
            ChannelId: item.ChannelId,
            StartAt: item.StartAt,
            EndAt: item.EndAt,
            Name: isHalfWidth ? item.HalfWidthName : item.Name)
        {
            RuleId = item.RuleId,
            Tags = item.Tags,
            ParentDirectoryName = item.ParentDirectoryName,
            Directory = item.Directory,
            RecordedFormat = item.RecordedFormat,
            EncodeMode1 = item.EncodeMode1,
            EncodeParentDirectoryName1 = item.EncodeParentDirectoryName1,
            EncodeDirectory1 = item.EncodeDirectory1,
            EncodeMode2 = item.EncodeMode2,
            EncodeParentDirectoryName2 = item.EncodeParentDirectoryName2,
            // EPGStation 2.10.0 does not emit encodeDirectory2 because of an upstream mapper typo.
            EncodeDirectory2 = null,
            EncodeMode3 = item.EncodeMode3,
            EncodeParentDirectoryName3 = item.EncodeParentDirectoryName3,
            EncodeDirectory3 = item.EncodeDirectory3,
            ProgramId = item.ProgramId,
            Description = isHalfWidth ? item.HalfWidthDescription : item.Description,
            Extended = isHalfWidth ? item.HalfWidthExtended : item.Extended,
            // Reserve conversion has different upstream behavior: no half-width value means omission.
            RawExtended = isHalfWidth ? item.HalfWidthRawExtended : item.RawExtended,
            Genre1 = item.Genre1,
            SubGenre1 = item.SubGenre1,
            Genre2 = item.Genre2,
            SubGenre2 = item.SubGenre2,
            Genre3 = item.Genre3,
            SubGenre3 = item.SubGenre3,
            VideoType = item.VideoType,
            VideoResolution = item.VideoResolution,
            VideoStreamContent = item.VideoStreamContent,
            VideoComponentType = item.VideoComponentType,
            AudioSamplingRate = item.AudioSamplingRate,
            // EPGStation 2.10.0 declares this field but its reserve mapper never emits it.
            AudioComponentType = null
        };

    private static UrlSchemeInfoResponse ToResponse(this UrlSchemeInfo info) =>
        new()
        {
            Ios = info.Ios,
            Android = info.Android,
            Mac = info.Mac,
            Win = info.Win
        };
}
