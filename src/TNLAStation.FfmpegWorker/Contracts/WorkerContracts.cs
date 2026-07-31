namespace TNLAStation.FfmpegWorker.Contracts;

public sealed record ProbeRequest(string Path);

public sealed record ProbeResponse(double? DurationSeconds);

public sealed record ThumbnailRequest(string InputPath, string OutputPath, int Width, int? Height, double PositionSeconds, string? Command = null);

public sealed record ThumbnailResponse(bool Success);

public sealed record EncodeRequest(
    string InputPath,
    string OutputPath,
    IReadOnlyList<string> Arguments,
    string? Command = null,
    double? RateTimeoutMultiplier = null,
    IReadOnlyDictionary<string, string>? EnvironmentVariables = null);

/// <summary>chunked NDJSON で 1 行ずつ流れる進捗。最後の 1 行だけ Done が true になる。</summary>
public sealed record EncodeProgress(bool Done, bool Succeeded, int? Percent, string? Log, string? Deferred = null);

public sealed record HlsLiveStartRequest(
    long StreamId,
    long ChannelId,
    int Height,
    string VideoBitrate,
    string AudioBitrate,
    int SegmentSeconds,
    int? Priority = null,
    string? Command = null);

public sealed record LowLatencyLiveStartRequest(
    long StreamId,
    long ChannelId,
    int Height,
    string VideoBitrate,
    string AudioBitrate,
    int? Priority = null);

public sealed record HlsRecordedStartRequest(
    long StreamId,
    string Path,
    int Height,
    string VideoBitrate,
    string AudioBitrate,
    int SegmentSeconds,
    double PlayPosition,
    string? Command = null,
    bool IsTransportStream = true);

/// <summary>RecentOutput は走っている間も返す。遅い理由はここにしか出ない。</summary>
public sealed record HlsStatusResponse(
    bool Found,
    bool IsRunning,
    string? LastError,
    string? RecentOutput);

public sealed record HlsStartResponse(string? WorkerBaseUrl);

public sealed record TranscodeLiveRequest(
    long ChannelId,
    int Height,
    string VideoBitrate,
    string AudioBitrate,
    IReadOnlyList<string> FormatArguments,
    int? Priority = null,
    string? Command = null);

public sealed record TranscodeRecordedRequest(
    string Path,
    int Height,
    string VideoBitrate,
    string AudioBitrate,
    IReadOnlyList<string> FormatArguments,
    double PlayPosition,
    string? Command = null,
    bool IsTransportStream = true);
