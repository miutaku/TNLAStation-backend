using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TNLAStation.Application.Abstractions;
using TNLAStation.Application.Models;
using TNLAStation.Domain;
using TNLAStation.Infrastructure.Configuration;
using TNLAStation.Infrastructure.Mirakurun;

namespace TNLAStation.Infrastructure.Streaming;

/// <summary>
/// ライブ視聴の実体。Mirakurun から届く MPEG-TS を ffmpeg に流し込み、ブラウザーが再生できる
/// HLS に作り替える。地上波の映像は MPEG-2 なので、再多重化では済まず必ず変換が要る。
/// </summary>
public sealed partial class HlsStreamManager : ILiveStreamService, IStreamRepository, IAsyncDisposable
{
    /// <summary>
    /// 設定が無いときの出力。mp4 は fragmented にする。長さの分かる普通の mp4 は、
    /// 全部書き終わるまで再生を始められないので、流しながらでは使えない。
    /// </summary>
    private static readonly StreamFormatOptions[] DefaultFormats =
    [
        new()
        {
            Name = "mp4",
            ContentType = "video/mp4",
            Arguments =
            [
                "-c:v", "libx264", "-preset", "veryfast", "-profile:v", "main",
                "-c:a", "aac",
                "-movflags", "frag_keyframe+empty_moov+default_base_moof",
                "-f", "mp4",
            ],
        },
        new()
        {
            Name = "webm",
            ContentType = "video/webm",
            // VP8 を実時間で作る。画質より間に合うことを優先する設定で、これでも重い。
            Arguments =
            [
                "-c:v", "libvpx", "-deadline", "realtime", "-cpu-used", "8",
                "-c:a", "libopus",
                "-f", "webm",
            ],
        },
        new()
        {
            Name = "m2tsll",
            ContentType = "video/mp2t",
            // 遅れを詰めた MPEG-TS。溜め込まずに出す。
            Arguments =
            [
                "-c:v", "libx264", "-preset", "ultrafast", "-tune", "zerolatency", "-profile:v", "main",
                "-c:a", "aac",
                "-flush_packets", "1",
                "-f", "mpegts",
            ],
        },
    ];

    private static readonly LiveStreamModeOptions[] DefaultModes =
    [
        new() { Name = "720p", Height = 720, VideoBitrate = "3000k", AudioBitrate = "192k" },
        new() { Name = "480p", Height = 480, VideoBitrate = "1500k", AudioBitrate = "128k" },
        new() { Name = "360p", Height = 360, VideoBitrate = "700k", AudioBitrate = "96k" },
    ];

    private readonly ConcurrentDictionary<long, HlsStreamSession> sessions = new();
    private readonly SemaphoreSlim startGate = new(1, 1);
    private readonly IMirakurunClient mirakurun;
    private readonly IEpgRepository epg;
    private readonly IVideoFileRepository videoFiles;
    private readonly StreamingOptions options;
    private readonly TimeProvider timeProvider;
    private readonly ILogger<HlsStreamManager> logger;
    private readonly ITimer reaper;
    private long lastStreamId;

    public HlsStreamManager(
        IMirakurunClient mirakurun,
        IEpgRepository epg,
        IVideoFileRepository videoFiles,
        IOptions<StreamingOptions> options,
        TimeProvider timeProvider,
        ILogger<HlsStreamManager> logger)
    {
        ArgumentNullException.ThrowIfNull(options);

        this.mirakurun = mirakurun;
        this.epg = epg;
        this.videoFiles = videoFiles;
        this.options = options.Value;
        this.timeProvider = timeProvider;
        this.logger = logger;
        reaper = timeProvider.CreateTimer(
            _ => ReapIdleSessions(),
            state: null,
            TimeSpan.FromSeconds(10),
            TimeSpan.FromSeconds(10));
    }

    /// <summary>
    /// 配信ファイルの置き場。プレイリストとセグメントはここから静的に配る。
    /// </summary>
    public string WorkDirectory => options.WorkDirectory;

    public async ValueTask<long> StartHlsAsync(long channelId, int mode, CancellationToken cancellationToken)
    {
        EpgChannel channel = await epg.GetChannelAsync(channelId, cancellationToken)
            ?? throw new LiveStreamException("ChannelIsNotFound");
        LiveStreamModeOptions quality = ResolveMode(mode);

        await startGate.WaitAsync(cancellationToken);
        HlsStreamSession session;
        try
        {
            if (sessions.Count >= Math.Max(1, options.MaxConcurrentStreams))
            {
                throw new LiveStreamException("StreamIsFull");
            }

            session = await StartSessionAsync(channel, mode, quality, cancellationToken);
            sessions[session.StreamId] = session;
        }
        finally
        {
            startGate.Release();
        }

        try
        {
            await session.WaitForPlaylistAsync(cancellationToken);
        }
        catch
        {
            sessions.TryRemove(session.StreamId, out _);
            await session.DisposeAsync();
            throw;
        }

        LogStreamStarted(logger, session.StreamId, channel.Name, quality.Name);
        return session.StreamId;
    }

    public async ValueTask<long> StartRecordedHlsAsync(
        long videoFileId,
        double playPosition,
        int mode,
        CancellationToken cancellationToken)
    {
        VideoFileLocation file = await videoFiles.GetAsync(videoFileId, cancellationToken)
            ?? throw new LiveStreamException("VideoFileIsNotFound");
        if (!File.Exists(file.FullPath))
        {
            throw new LiveStreamException("VideoFileIsNotFound");
        }

        LiveStreamModeOptions quality = ResolveMode(mode);

        await startGate.WaitAsync(cancellationToken);
        HlsStreamSession session;
        try
        {
            if (sessions.Count >= Math.Max(1, options.MaxConcurrentStreams))
            {
                throw new LiveStreamException("StreamIsFull");
            }

            Directory.CreateDirectory(options.WorkDirectory);
            long streamId = Interlocked.Increment(ref lastStreamId);
            session = new HlsStreamSession(
                streamId,
                channelId: 0,
                file.Name,
                mode,
                timeProvider.GetUtcNow(),
                options,
                source: null,
                CreateRecordedArguments(streamId, quality, file.FullPath, playPosition),
                logger)
            {
                VideoFileId = videoFileId,
            };
            session.Start();
            sessions[streamId] = session;
        }
        finally
        {
            startGate.Release();
        }

        try
        {
            await session.WaitForPlaylistAsync(cancellationToken);
        }
        catch
        {
            sessions.TryRemove(session.StreamId, out _);
            await session.DisposeAsync();
            throw;
        }

        LogStreamStarted(logger, session.StreamId, file.Name, quality.Name);
        return session.StreamId;
    }

    public bool Keep(long streamId)
    {
        if (!sessions.TryGetValue(streamId, out HlsStreamSession? session))
        {
            return false;
        }

        session.LastKeepAt = timeProvider.GetUtcNow();
        return true;
    }

    public async ValueTask<bool> StopAsync(long streamId)
    {
        if (!sessions.TryRemove(streamId, out HlsStreamSession? session))
        {
            return false;
        }

        await session.DisposeAsync();
        LogStreamStopped(logger, streamId, "requested");
        return true;
    }

    /// <summary>
    /// 変換しながら 1 本の流れとして配る。HLS と違って途中のファイルを作らないので、
    /// 遅れは小さいが、途中から見ることも巻き戻すこともできない。
    /// </summary>
    public async ValueTask<TranscodedOutput> OpenTranscodedLiveAsync(
        long channelId,
        string format,
        int mode,
        CancellationToken cancellationToken)
    {
        _ = await epg.GetChannelAsync(channelId, cancellationToken)
            ?? throw new LiveStreamException("ChannelIsNotFound");
        StreamFormatOptions output = ResolveFormat(format);
        LiveStreamModeOptions quality = ResolveMode(mode);

        Stream source = await mirakurun.OpenServiceStreamAsync(channelId, cancellationToken);
        try
        {
            ProcessOutputStream stream = StartTranscode("pipe:0", quality, output, source, playPosition: null);
            stream.StartPump();
            return new TranscodedOutput(stream, output.ContentType);
        }
        catch
        {
            await source.DisposeAsync();
            throw;
        }
    }

    public async ValueTask<TranscodedOutput> OpenTranscodedRecordedAsync(
        long videoFileId,
        string format,
        int mode,
        double playPosition,
        CancellationToken cancellationToken)
    {
        VideoFileLocation file = await videoFiles.GetAsync(videoFileId, cancellationToken)
            ?? throw new LiveStreamException("VideoFileIsNotFound");
        if (!File.Exists(file.FullPath))
        {
            throw new LiveStreamException("VideoFileIsNotFound");
        }

        StreamFormatOptions output = ResolveFormat(format);
        LiveStreamModeOptions quality = ResolveMode(mode);
        return new TranscodedOutput(
            StartTranscode(file.FullPath, quality, output, source: null, playPosition),
            output.ContentType);
    }

    private ProcessOutputStream StartTranscode(
        string input,
        LiveStreamModeOptions quality,
        StreamFormatOptions output,
        Stream? source,
        double? playPosition)
    {
        var startInfo = new ProcessStartInfo(options.FfmpegPath)
        {
            RedirectStandardInput = source is not null,
            RedirectStandardOutput = true,
            RedirectStandardError = false,
            UseShellExecute = false,
        };
        foreach (string argument in CreateTranscodeArguments(input, quality, output, playPosition))
        {
            startInfo.ArgumentList.Add(argument);
        }

        Process process = Process.Start(startInfo)
            ?? throw new LiveStreamException("StreamProcessStartFailed");
        return new ProcessOutputStream(process, source);
    }

    private static string[] CreateTranscodeArguments(
        string input,
        LiveStreamModeOptions quality,
        StreamFormatOptions output,
        double? playPosition)
    {
        var arguments = new List<string>
        {
            "-hide_banner",
            "-loglevel", "error",
            "-fflags", "+discardcorrupt",
        };

        if (input == "pipe:0")
        {
            // 流れてくる先を戻して読み直せないので、ffmpeg は形式の判別に失敗することがある。
            // 失敗すると 1 バイトも出さずに待ち続けるので、放送波だと分かっている以上は明示する。
            arguments.AddRange(["-f", "mpegts"]);
        }

        arguments.AddRange(["-analyzeduration", "10M", "-probesize", "32M"]);

        if (playPosition is { } position and > 0)
        {
            arguments.Add("-ss");
            arguments.Add(position.ToString("0.###", CultureInfo.InvariantCulture));
        }

        arguments.AddRange(["-dual_mono_mode", "main", "-i", input]);
        arguments.AddRange(["-map", "0:v:0", "-map", "0:a:0", "-ignore_unknown", "-sn", "-dn"]);
        arguments.AddRange([
            "-vf", $"yadif,scale=-2:{quality.Height.ToString(CultureInfo.InvariantCulture)}",
            "-b:v", quality.VideoBitrate,
            "-maxrate", quality.VideoBitrate,
            "-bufsize", quality.VideoBitrate,
            "-b:a", quality.AudioBitrate,
            "-ar", "48000",
            "-ac", "2",
        ]);
        arguments.AddRange(output.Arguments);
        arguments.Add("pipe:1");
        return [.. arguments];
    }

    private StreamFormatOptions ResolveFormat(string name)
    {
        IReadOnlyList<StreamFormatOptions> formats = options.Formats.Count > 0 ? options.Formats : DefaultFormats;
        return formats.FirstOrDefault(format => string.Equals(format.Name, name, StringComparison.OrdinalIgnoreCase))
            ?? throw new LiveStreamException("StreamFormatIsNotFound");
    }

    public async ValueTask<Stream> OpenLiveStreamAsync(long channelId, CancellationToken cancellationToken)
    {
        _ = await epg.GetChannelAsync(channelId, cancellationToken)
            ?? throw new LiveStreamException("ChannelIsNotFound");
        return await mirakurun.OpenServiceStreamAsync(channelId, cancellationToken);
    }

    public async ValueTask StopAllAsync()
    {
        foreach (long streamId in sessions.Keys)
        {
            await StopAsync(streamId);
        }
    }

    public async ValueTask<IReadOnlyList<StreamSession>> ListAsync(CancellationToken cancellationToken)
    {
        HlsStreamSession[] active = [.. sessions.Values];
        if (active.Length == 0)
        {
            return [];
        }

        var result = new List<StreamSession>(active.Length);
        DateTimeOffset now = timeProvider.GetUtcNow();
        foreach (HlsStreamSession session in active.OrderBy(item => item.StreamId))
        {
            if (session.VideoFileId is { } videoFileId)
            {
                // 録画済みの再生には放送局も番組もない。あるのは何を再生しているかだけ。
                result.Add(new StreamSession(
                    session.StreamId,
                    "RecordedHLS",
                    session.Mode,
                    IsEnable: session.IsRunning,
                    ChannelId: 0,
                    session.ChannelName,
                    VideoFileId: videoFileId));
                continue;
            }

            // 番組は視聴中に切り替わる。開始時の番組を持ち回すと、次の番組になっても
            // 前の番組名と時刻が残ってしまうので、その都度引き直す。
            EpgProgram? program = await FindBroadcastingProgramAsync(session.ChannelId, now, cancellationToken);
            result.Add(new StreamSession(
                session.StreamId,
                "LiveHLS",
                session.Mode,
                IsEnable: session.IsRunning,
                session.ChannelId,
                program?.Name ?? session.ChannelName,
                ProgramId: program?.Id,
                StartAt: (program?.StartAt ?? session.StartedAt).ToUnixTimeMilliseconds(),
                EndAt: (program?.EndAt ?? session.StartedAt.AddHours(1)).ToUnixTimeMilliseconds(),
                Description: program?.Description));
        }

        return result;
    }

    public async ValueTask DisposeAsync()
    {
        reaper.Dispose();
        foreach (long streamId in sessions.Keys)
        {
            if (sessions.TryRemove(streamId, out HlsStreamSession? session))
            {
                await session.DisposeAsync();
            }
        }

        startGate.Dispose();
    }

    private async ValueTask<HlsStreamSession> StartSessionAsync(
        EpgChannel channel,
        int mode,
        LiveStreamModeOptions quality,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(options.WorkDirectory);
        long streamId = Interlocked.Increment(ref lastStreamId);
        Stream source = await mirakurun.OpenServiceStreamAsync(channel.Id, cancellationToken);
        try
        {
            var session = new HlsStreamSession(
                streamId,
                channel.Id,
                channel.Name,
                mode,
                timeProvider.GetUtcNow(),
                options,
                source,
                CreateArguments(streamId, quality),
                logger);
            session.Start();
            return session;
        }
        catch
        {
            await source.DisposeAsync();
            throw;
        }
    }

    private async ValueTask<EpgProgram?> FindBroadcastingProgramAsync(
        long channelId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<EpgProgram> programs = await epg.FindProgramsAsync(
            new EpgScheduleQuery(now, now, ChannelId: channelId),
            cancellationToken);
        return programs.Count == 0 ? null : programs[0];
    }

    private LiveStreamModeOptions ResolveMode(int mode)
    {
        IReadOnlyList<LiveStreamModeOptions> modes = options.LiveModes.Count > 0 ? options.LiveModes : DefaultModes;
        return mode >= 0 && mode < modes.Count
            ? modes[mode]
            : throw new LiveStreamException("StreamModeIsNotFound");
    }

    private string[] CreateArguments(long streamId, LiveStreamModeOptions quality)
    {
        string segmentSeconds = options.SegmentSeconds.ToString(CultureInfo.InvariantCulture);
        string playlist = Path.Combine(options.WorkDirectory, $"stream{streamId}.m3u8");
        string segments = Path.Combine(options.WorkDirectory, $"stream{streamId}-%d.ts");

        return
        [
            "-hide_banner",
            "-loglevel", "warning",
            // 放送波は欠けた TS が混ざる。落とさずに読み飛ばす。
            "-fflags", "+discardcorrupt",
            "-analyzeduration", "10M",
            "-probesize", "32M",
            // 副音声つきの番組で、主音声だけを取り出す。
            "-dual_mono_mode", "main",
            "-i", "pipe:0",
            "-map", "0:v:0",
            "-map", "0:a:0",
            "-ignore_unknown",
            "-sn",
            "-dn",
            "-c:v", "libx264",
            "-preset", "veryfast",
            "-profile:v", "main",
            // インターレース解除を挟まないと、動きのある場面が櫛状に割れる。
            "-vf", $"yadif,scale=-2:{quality.Height.ToString(CultureInfo.InvariantCulture)}",
            "-b:v", quality.VideoBitrate,
            "-maxrate", quality.VideoBitrate,
            "-bufsize", quality.VideoBitrate,
            // セグメントの先頭は必ずキーフレームにする。でないと切り替えで映像が崩れる。
            "-force_key_frames", $"expr:gte(t,n_forced*{segmentSeconds})",
            "-c:a", "aac",
            "-ar", "48000",
            "-b:a", quality.AudioBitrate,
            "-ac", "2",
            "-max_muxing_queue_size", "1024",
            "-f", "hls",
            "-hls_time", segmentSeconds,
            "-hls_list_size", "6",
            "-hls_delete_threshold", "1",
            "-hls_flags", "delete_segments+temp_file+omit_endlist",
            "-hls_segment_filename", segments,
            "-y", playlist,
        ];
    }

    /// <summary>
    /// 録画済みは ffmpeg にファイルを直接読ませる。頭出しは入力の前に置く。後ろに置くと、
    /// 指定した位置まで復号してから捨てることになり、長い録画では待たされる。
    /// </summary>
    private string[] CreateRecordedArguments(
        long streamId,
        LiveStreamModeOptions quality,
        string path,
        double playPosition)
    {
        string segmentSeconds = options.SegmentSeconds.ToString(CultureInfo.InvariantCulture);
        string playlist = Path.Combine(options.WorkDirectory, $"stream{streamId}.m3u8");
        string segments = Path.Combine(options.WorkDirectory, $"stream{streamId}-%d.ts");

        return
        [
            "-hide_banner",
            "-loglevel", "warning",
            "-fflags", "+discardcorrupt",
            "-analyzeduration", "10M",
            "-probesize", "32M",
            "-ss", playPosition.ToString("0.###", CultureInfo.InvariantCulture),
            "-dual_mono_mode", "main",
            "-i", path,
            "-map", "0:v:0",
            "-map", "0:a:0",
            "-ignore_unknown",
            "-sn",
            "-dn",
            "-c:v", "libx264",
            "-preset", "veryfast",
            "-profile:v", "main",
            "-vf", $"yadif,scale=-2:{quality.Height.ToString(CultureInfo.InvariantCulture)}",
            "-b:v", quality.VideoBitrate,
            "-maxrate", quality.VideoBitrate,
            "-bufsize", quality.VideoBitrate,
            "-force_key_frames", $"expr:gte(t,n_forced*{segmentSeconds})",
            "-c:a", "aac",
            "-ar", "48000",
            "-b:a", quality.AudioBitrate,
            "-ac", "2",
            "-max_muxing_queue_size", "1024",
            "-f", "hls",
            "-hls_time", segmentSeconds,
            // 録画済みは頭から順に見るので、過ぎたセグメントも残す。巻き戻せなくなる。
            "-hls_list_size", "0",
            "-hls_flags", "temp_file",
            "-hls_segment_filename", segments,
            "-y", playlist,
        ];
    }

    private void ReapIdleSessions()
    {
        DateTimeOffset deadline = timeProvider.GetUtcNow().AddSeconds(-Math.Max(5, options.IdleTimeoutSeconds));
        foreach (HlsStreamSession session in sessions.Values)
        {
            bool expired = session.LastKeepAt < deadline;
            if (!expired && session.IsRunning)
            {
                continue;
            }

            if (sessions.TryRemove(session.StreamId, out HlsStreamSession? removed))
            {
                LogStreamStopped(logger, removed.StreamId, expired ? "idle" : "ffmpeg exited");
                _ = removed.DisposeAsync().AsTask();
            }
        }
    }

    [LoggerMessage(
        EventId = 2000,
        Level = LogLevel.Information,
        Message = "Started live stream {StreamId} for {ChannelName} at {Quality}")]
    private static partial void LogStreamStarted(
        ILogger logger,
        long streamId,
        string channelName,
        string quality);

    [LoggerMessage(
        EventId = 2001,
        Level = LogLevel.Information,
        Message = "Stopped live stream {StreamId} ({Reason})")]
    private static partial void LogStreamStopped(ILogger logger, long streamId, string reason);
}
