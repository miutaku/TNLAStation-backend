using System.Collections.Concurrent;
using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TNLAStation.Application.Abstractions;
using TNLAStation.Application.Models;
using TNLAStation.Domain;
using TNLAStation.Infrastructure.Configuration;
using TNLAStation.Infrastructure.Configuration.EpgStation;
using TNLAStation.Infrastructure.Mirakurun;

namespace TNLAStation.Infrastructure.Streaming;

/// <summary>
/// ライブ視聴の制御面。実際に ffmpeg を回すのは ffmpeg-worker で、ここは開始・停止の指示と
/// セッションの帳簿付けだけを持つ。HLS のプレイリスト・セグメントは worker と共有するボリューム
/// (<see cref="StreamingOptions.WorkDirectory"/>) 越しに直接読む。中継してファイルを二重に
/// 転送しない。
/// </summary>
public sealed partial class RemoteLiveStreamService : ILiveStreamService, IStreamRepository, IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ConcurrentDictionary<long, SessionRecord> sessions = new();
    private readonly object reaperGate = new();
    private readonly SemaphoreSlim startGate = new(1, 1);
    private readonly HttpClient worker;
    private readonly StreamingWorkerSelector workerSelector;
    private readonly IMirakurunClient mirakurun;
    private readonly IEpgRepository epg;
    private readonly IVideoFileRepository videoFiles;
    private readonly StreamingOptions options;
    private readonly IEpgStationConfigAccessor configuration;
    private readonly MirakurunOptions mirakurunOptions;
    private readonly TimeProvider timeProvider;
    private readonly ILogger<RemoteLiveStreamService> logger;
    private readonly ITimer reaper;
    private bool disposing;
    private Task reaperTask = Task.CompletedTask;
    private long lastStreamId;

    public RemoteLiveStreamService(
        HttpClient worker,
        StreamingWorkerSelector workerSelector,
        IMirakurunClient mirakurun,
        IEpgRepository epg,
        IVideoFileRepository videoFiles,
        IOptions<StreamingOptions> options,
        IEpgStationConfigAccessor configuration,
        IOptions<MirakurunOptions> mirakurunOptions,
        TimeProvider timeProvider,
        ILogger<RemoteLiveStreamService> logger)
    {
        ArgumentNullException.ThrowIfNull(options);

        this.worker = worker;
        this.workerSelector = workerSelector;
        this.mirakurun = mirakurun;
        this.epg = epg;
        this.videoFiles = videoFiles;
        this.options = options.Value;
        this.configuration = configuration;
        this.mirakurunOptions = mirakurunOptions.Value;
        this.timeProvider = timeProvider;
        this.logger = logger;
        reaper = timeProvider.CreateTimer(
            _ => QueueReap(),
            state: null,
            TimeSpan.FromSeconds(10),
            TimeSpan.FromSeconds(10));
    }

    public async ValueTask<long> StartHlsAsync(long channelId, int mode, CancellationToken cancellationToken)
    {
        EpgStationStreamingCmd streamCommand = ResolveLiveConfig("hls", mode);
        EpgChannel channel = await epg.GetChannelAsync(channelId, cancellationToken)
            ?? throw new LiveStreamException("ChannelIsNotFound");
        LiveStreamModeOptions quality = ResolveMode(mode);

        await startGate.WaitAsync(cancellationToken);
        long streamId;
        try
        {
            await EnsureRoomForOneMoreAsync(cancellationToken);

            streamId = Interlocked.Increment(ref lastStreamId);
            var record = new SessionRecord(streamId, channelId, channel.Name, mode, videoFileId: null, timeProvider.GetUtcNow());
            sessions[streamId] = record;

            Uri workerBaseAddress = await SelectWorkerAsync(cancellationToken);
            using HttpResponseMessage response = await worker.PostAsJsonAsync(
                new Uri(workerBaseAddress, "streams/hls/live"),
                new HlsLiveStartRequest(streamId, channelId, quality.Height, quality.VideoBitrate, quality.AudioBitrate, options.SegmentSeconds, mirakurunOptions.StreamingPriority, streamCommand.Cmd),
                JsonOptions,
                cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                sessions.TryRemove(streamId, out _);
                throw new LiveStreamException("StreamProcessStartFailed");
            }

            record.WorkerBaseAddress = await ReadWorkerBaseAddressAsync(response, cancellationToken);
        }
        finally
        {
            startGate.Release();
        }

        try
        {
            await WaitForPlaylistAsync(streamId, cancellationToken);
        }
        catch
        {
            await StopAsync(streamId);
            throw;
        }

        LogStreamStarted(logger, streamId, channel.Name, quality.Name);
        return streamId;
    }

    /// <summary>
    /// publish 先は配信サーバーなので、backend からはプレイリストの生成を確認できない。
    /// 待たずに返し、再生側で拾い直す。
    /// </summary>
    public async ValueTask<LowLatencyPlayback> StartLowLatencyAsync(long channelId, int mode, CancellationToken cancellationToken)
    {
        string template = options.LowLatencyHls?.PlaylistUrlTemplate is { Length: > 0 } configured
            ? configured
            : throw new LiveStreamException("LowLatencyHlsIsNotConfigured");
        EpgChannel channel = await epg.GetChannelAsync(channelId, cancellationToken)
            ?? throw new LiveStreamException("ChannelIsNotFound");
        LiveStreamModeOptions quality = ResolveMode(mode);

        await startGate.WaitAsync(cancellationToken);
        long streamId;
        try
        {
            await EnsureRoomForOneMoreAsync(cancellationToken);

            streamId = Interlocked.Increment(ref lastStreamId);
            var record = new SessionRecord(streamId, channelId, channel.Name, mode, videoFileId: null, timeProvider.GetUtcNow());
            sessions[streamId] = record;

            Uri workerBaseAddress = await SelectWorkerAsync(cancellationToken);
            using HttpResponseMessage response = await worker.PostAsJsonAsync(
                new Uri(workerBaseAddress, "streams/lowlatency/live"),
                new LowLatencyLiveStartRequest(streamId, channelId, quality.Height, quality.VideoBitrate, quality.AudioBitrate, mirakurunOptions.StreamingPriority),
                JsonOptions,
                cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                sessions.TryRemove(streamId, out _);
                throw new LiveStreamException("StreamProcessStartFailed");
            }

            record.WorkerBaseAddress = await ReadWorkerBaseAddressAsync(response, cancellationToken);
        }
        finally
        {
            startGate.Release();
        }

        LogStreamStarted(logger, streamId, channel.Name, quality.Name);
        return new LowLatencyPlayback(streamId, FormatPlaylistUrl(template, streamId));
    }

    /// <summary>
    /// 空きが無ければ理由の分かるエラーにする。上限は worker が CPU から報告した定員の合計で、
    /// 設定で明示されていればそちらを天井にする。worker へ問い合わせられないときは既存の
    /// セッション数を信じて通す — 数えられないことを理由に視聴を止める必要はない。
    /// </summary>
    private async Task EnsureRoomForOneMoreAsync(CancellationToken cancellationToken)
    {
        int limit = options.MaxConcurrentStreams > 0
            ? options.MaxConcurrentStreams
            : await workerSelector.ReadViewingCapacityAsync(worker, cancellationToken);
        if (limit > 0 && sessions.Count >= limit)
        {
            throw new LiveStreamException("StreamIsFull");
        }
    }

    public static string FormatPlaylistUrl(string template, long streamId) =>
        template.Replace("{streamId}", streamId.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);

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
        string command = ResolveRecordedConfig(file.Type, "hls", mode);

        await startGate.WaitAsync(cancellationToken);
        long streamId;
        try
        {
            await EnsureRoomForOneMoreAsync(cancellationToken);

            streamId = Interlocked.Increment(ref lastStreamId);
            var record = new SessionRecord(streamId, channelId: 0, file.Name, mode, videoFileId, timeProvider.GetUtcNow());
            sessions[streamId] = record;

            Uri workerBaseAddress = await SelectWorkerAsync(cancellationToken);
            using HttpResponseMessage response = await worker.PostAsJsonAsync(
                new Uri(workerBaseAddress, "streams/hls/recorded"),
                new HlsRecordedStartRequest(streamId, file.FullPath, quality.Height, quality.VideoBitrate, quality.AudioBitrate, options.SegmentSeconds, playPosition, command, IsTransportStream(file.Type)),
                JsonOptions,
                cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                sessions.TryRemove(streamId, out _);
                throw new LiveStreamException("StreamProcessStartFailed");
            }

            record.WorkerBaseAddress = await ReadWorkerBaseAddressAsync(response, cancellationToken);
        }
        finally
        {
            startGate.Release();
        }

        try
        {
            await WaitForPlaylistAsync(streamId, cancellationToken);
        }
        catch
        {
            await StopAsync(streamId);
            throw;
        }

        LogStreamStarted(logger, streamId, file.Name, quality.Name);
        return streamId;
    }

    public bool Keep(long streamId)
    {
        if (!sessions.TryGetValue(streamId, out SessionRecord? session))
        {
            return false;
        }

        session.LastKeepAt = timeProvider.GetUtcNow();
        return true;
    }

    public async ValueTask<bool> StopAsync(long streamId)
    {
        if (!sessions.TryRemove(streamId, out SessionRecord? session))
        {
            return false;
        }

        try
        {
            using HttpResponseMessage response = await worker.DeleteAsync(
                ResolveWorkerRequestUri(session, $"streams/hls/{streamId}"),
                CancellationToken.None);
        }
        catch (HttpRequestException exception)
        {
            LogStreamCleanupFailed(logger, streamId, exception);
        }

        LogStreamStopped(logger, streamId, "requested");
        return true;
    }

    /// <summary>
    /// 変換しながら 1 本の流れとして配る。ライブは worker が自分で Mirakurun から取りに行く。
    /// backend が受信して転送し直すと二重にネットワークへ乗せることになる。
    /// </summary>
    public async ValueTask<TranscodedOutput> OpenTranscodedLiveAsync(
        long channelId,
        string format,
        int mode,
        CancellationToken cancellationToken)
    {
        EpgStationStreamingCmd streamCommand = ResolveLiveConfig(format, mode);
        _ = await epg.GetChannelAsync(channelId, cancellationToken)
            ?? throw new LiveStreamException("ChannelIsNotFound");
        StreamFormatOptions output = ResolveFormat(format);
        LiveStreamModeOptions quality = ResolveMode(mode);

        var request = new HttpRequestMessage(HttpMethod.Post, "streams/transcode/live")
        {
            Content = JsonContent.Create(
                new TranscodeLiveRequest(channelId, quality.Height, quality.VideoBitrate, quality.AudioBitrate, output.Arguments, mirakurunOptions.StreamingPriority, streamCommand.Cmd),
                options: JsonOptions),
        };
        request.RequestUri = new Uri(await SelectWorkerAsync(cancellationToken), "streams/transcode/live");
        HttpResponseMessage response = await worker.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        try
        {
            response.EnsureSuccessStatusCode();
            Stream content = await response.Content.ReadAsStreamAsync(cancellationToken);
            return new TranscodedOutput(new ResponseOwningStream(response, content), output.ContentType);
        }
        catch
        {
            response.Dispose();
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
        string command = ResolveRecordedConfig(file.Type, format, mode);

        var request = new HttpRequestMessage(HttpMethod.Post, "streams/transcode/recorded")
        {
            Content = JsonContent.Create(
                new TranscodeRecordedRequest(file.FullPath, quality.Height, quality.VideoBitrate, quality.AudioBitrate, output.Arguments, playPosition, command, IsTransportStream(file.Type)),
                options: JsonOptions),
        };
        request.RequestUri = new Uri(await SelectWorkerAsync(cancellationToken), "streams/transcode/recorded");
        HttpResponseMessage response = await worker.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        try
        {
            response.EnsureSuccessStatusCode();
            Stream content = await response.Content.ReadAsStreamAsync(cancellationToken);
            return new TranscodedOutput(new ResponseOwningStream(response, content), output.ContentType);
        }
        catch
        {
            response.Dispose();
            throw;
        }
    }

    private StreamFormatOptions ResolveFormat(string name)
    {
        IReadOnlyList<StreamFormatOptions> formats = options.Formats ?? StreamingDefaults.Formats;
        return formats.FirstOrDefault(format => string.Equals(format.Name, name, StringComparison.OrdinalIgnoreCase))
            ?? throw new LiveStreamException("StreamFormatIsNotFound");
    }

    private EpgStationStreamingCmd ResolveLiveConfig(string format, int mode)
    {
        EpgStationLiveTsConfig? config = configuration.Current.Stream?.Live?.Ts;
        IReadOnlyList<EpgStationStreamingCmd>? commands = format.ToLowerInvariant() switch
        {
            "m2ts" => config?.M2Ts,
            "m2tsll" => config?.M2TsLl,
            "webm" => config?.Webm,
            "mp4" => config?.Mp4,
            "hls" => config?.Hls,
            _ => null,
        };

        if (commands is null || mode < 0 || mode >= commands.Count)
        {
            throw new LiveStreamException("ConfigIsUndefined");
        }

        return commands[mode];
    }

    private string ResolveRecordedConfig(string fileType, string format, int mode)
    {
        EpgStationRecordedStreamConfig? recorded = configuration.Current.Stream?.Recorded;
        EpgStationRecordedTsConfig? config = string.Equals(fileType, "encoded", StringComparison.Ordinal)
            ? recorded?.Encoded
            : recorded?.Ts;
        IReadOnlyList<EpgStationStreamingCmd>? commands = format.ToLowerInvariant() switch
        {
            "webm" => config?.Webm,
            "mp4" => config?.Mp4,
            "hls" => config?.Hls,
            _ => null,
        };
        if (commands is null || mode < 0 || mode >= commands.Count)
        {
            throw new LiveStreamException("ConfigIsUndefined");
        }

        return commands[mode].Cmd ?? throw new LiveStreamException("CmdIsUndefined");
    }

    private static bool IsTransportStream(string fileType) =>
        !string.Equals(fileType, "encoded", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 放送をそのまま流す。変換しないので ffmpeg は要らず、backend が直接 Mirakurun から取る。
    /// </summary>
    public async ValueTask<Stream> OpenLiveStreamAsync(long channelId, int mode, CancellationToken cancellationToken)
    {
        EpgStationStreamingCmd streamCommand = ResolveLiveConfig("m2ts", mode);
        _ = await epg.GetChannelAsync(channelId, cancellationToken)
            ?? throw new LiveStreamException("ChannelIsNotFound");
        if (streamCommand.Cmd is not null)
        {
            return (await OpenTranscodedLiveAsync(channelId, "m2ts", mode, cancellationToken)).Content;
        }

        return await mirakurun.OpenServiceStreamAsync(channelId, cancellationToken, mirakurunOptions.StreamingPriority);
    }

    /// <summary>
    /// 流しっぱなしの配信を list へ載せる。keep は届かないので、収集対象から外す
    /// (畳まれるまで生き続ける)。
    /// </summary>
    public ValueTask<IAsyncDisposable> TrackDirectStreamAsync(
        DirectStreamDescriptor descriptor,
        CancellationToken cancellationToken)
    {
        long streamId = Interlocked.Increment(ref lastStreamId);
        var record = new SessionRecord(
            streamId,
            descriptor.ChannelId,
            string.Empty,
            descriptor.Mode,
            descriptor.VideoFileId,
            timeProvider.GetUtcNow())
        {
            DirectType = descriptor.Type,
            Client = descriptor.Client,
        };
        sessions[streamId] = record;
        LogStreamStarted(logger, streamId, descriptor.Client ?? descriptor.Type, descriptor.Type);
        return ValueTask.FromResult<IAsyncDisposable>(new DirectStreamScope(this, streamId));
    }

    private void ForgetDirectStream(long streamId)
    {
        if (sessions.TryRemove(streamId, out _))
        {
            LogStreamStopped(logger, streamId, "client disconnected");
        }
    }

    /// <summary>読み手が閉じたら list から外す。掴んだままにすると視聴中が残り続ける。</summary>
    private sealed class DirectStreamScope(RemoteLiveStreamService owner, long streamId) : IAsyncDisposable
    {
        private int disposed;

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref disposed, 1) == 0)
            {
                owner.ForgetDirectStream(streamId);
            }

            return ValueTask.CompletedTask;
        }
    }

    public async ValueTask StopAllAsync()
    {
        var failures = new List<Exception>();
        foreach (long streamId in sessions.Keys)
        {
            try
            {
                await StopAsync(streamId);
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }
        }

        if (failures.Count > 0)
        {
            throw new AggregateException("One or more streams could not be stopped.", failures);
        }
    }

    public async ValueTask<IReadOnlyList<StreamSession>> ListAsync(CancellationToken cancellationToken)
    {
        SessionRecord[] active = [.. sessions.Values];
        if (active.Length == 0)
        {
            return [];
        }

        var result = new List<StreamSession>(active.Length);
        DateTimeOffset now = timeProvider.GetUtcNow();
        foreach (SessionRecord session in active.OrderBy(item => item.StreamId))
        {
            if (session.DirectType is { } directType)
            {
                EpgProgram? airing = session.ChannelId == 0
                    ? null
                    : await FindBroadcastingProgramAsync(session.ChannelId, now, cancellationToken);
                result.Add(new StreamSession(
                    session.StreamId,
                    session.VideoFileId is null ? "LiveStream" : "RecordedStream",
                    session.Mode,
                    IsEnable: true,
                    session.ChannelId,
                    airing?.Name ?? directType,
                    ProgramId: airing?.Id,
                    VideoFileId: session.VideoFileId,
                    StartAt: (airing?.StartAt ?? session.StartedAt).ToUnixTimeMilliseconds(),
                    EndAt: (airing?.EndAt ?? session.StartedAt.AddHours(1)).ToUnixTimeMilliseconds(),
                    Description: airing?.Description,
                    Client: session.Client));
                continue;
            }

            if (session.VideoFileId is { } videoFileId)
            {
                result.Add(new StreamSession(
                    session.StreamId,
                    "RecordedHLS",
                    session.Mode,
                    IsEnable: session.IsRunningCached,
                    ChannelId: 0,
                    session.ChannelName,
                    VideoFileId: videoFileId));
                continue;
            }

            EpgProgram? program = await FindBroadcastingProgramAsync(session.ChannelId, now, cancellationToken);
            result.Add(new StreamSession(
                session.StreamId,
                "LiveHLS",
                session.Mode,
                IsEnable: session.IsRunningCached,
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
        lock (reaperGate)
        {
            disposing = true;
        }

        reaper.Dispose();
        Task pendingReap;
        lock (reaperGate)
        {
            pendingReap = reaperTask;
        }

        try
        {
            await pendingReap;
            await StopAllAsync();
        }
        finally
        {
            startGate.Dispose();
        }
    }

    private async Task<EpgProgram?> FindBroadcastingProgramAsync(
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
        IReadOnlyList<LiveStreamModeOptions> modes = options.LiveModes ?? StreamingDefaults.LiveModes;
        return mode >= 0 && mode < modes.Count
            ? modes[mode]
            : throw new LiveStreamException("StreamModeIsNotFound");
    }

    private string PlaylistPath(long streamId) => Path.Combine(options.WorkDirectory, $"stream{streamId}.m3u8");

    /// <summary>
    /// プレイリストが書かれるまで待つ。ここで待たずに stream id を返すと、画面は
    /// まだ存在しない .m3u8 を取りに行って再生に失敗する。共有ボリュームを直接見るので、
    /// worker との間に追加の往復は要らない。
    /// </summary>
    private async Task WaitForPlaylistAsync(long streamId, CancellationToken cancellationToken)
    {
        if (!sessions.TryGetValue(streamId, out SessionRecord? session))
        {
            throw new LiveStreamException("StreamIsNotFound");
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, options.PlaylistTimeoutSeconds)));
        string playlistPath = PlaylistPath(streamId);

        try
        {
            while (!File.Exists(playlistPath))
            {
                HlsStatusResponse? status = await TryGetStatusAsync(session, cancellationToken);
                if (status is { Found: true, IsRunning: false })
                {
                    throw new LiveStreamException(status.LastError ?? "ffmpeg exited before producing a playlist");
                }

                await Task.Delay(TimeSpan.FromMilliseconds(200), timeout.Token);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // 生きている ffmpeg の出力は status からしか取れない。添えないと理由が残らない。
            HlsStatusResponse? status = await TryGetStatusAsync(session, cancellationToken);
            throw new LiveStreamException(status?.RecentOutput is { Length: > 0 } output
                ? $"ffmpeg produced no playlist in time: {output}"
                : "ffmpeg produced no playlist in time");
        }
    }

    private async Task<HlsStatusResponse?> TryGetStatusAsync(
        SessionRecord session,
        CancellationToken cancellationToken)
    {
        try
        {
            return await worker.GetFromJsonAsync<HlsStatusResponse>(
                ResolveWorkerRequestUri(session, $"streams/hls/{session.StreamId}"),
                JsonOptions,
                cancellationToken);
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    private void QueueReap()
    {
        lock (reaperGate)
        {
            if (disposing)
            {
                return;
            }

            if (!reaperTask.IsCompleted)
            {
                return;
            }

            reaperTask = ReapIdleSessionsAsync();
        }
    }

    private async Task ReapIdleSessionsAsync()
    {
        DateTimeOffset deadline = timeProvider.GetUtcNow().AddSeconds(-Math.Max(5, options.IdleTimeoutSeconds));
        foreach (SessionRecord session in sessions.Values)
        {
            // 直接配信は worker を介さず、keep も来ない。読み手が切るまで生かす。
            if (session.DirectType is not null)
            {
                continue;
            }

            HlsStatusResponse? status = await TryGetStatusAsync(session, CancellationToken.None);
            session.IsRunningCached = status?.IsRunning ?? false;

            bool expired = session.LastKeepAt < deadline;
            if (!expired && session.IsRunningCached)
            {
                continue;
            }

            if (sessions.TryRemove(session.StreamId, out _))
            {
                LogStreamStopped(logger, session.StreamId, expired ? "idle" : "ffmpeg exited");
                try
                {
                    using HttpResponseMessage response = await worker.DeleteAsync(
                        ResolveWorkerRequestUri(session, $"streams/hls/{session.StreamId}"),
                        CancellationToken.None);
                }
                catch (Exception exception)
                {
                    LogStreamCleanupFailed(logger, session.StreamId, exception);
                }
            }
        }
    }

    private static async Task<Uri?> ReadWorkerBaseAddressAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (response.Content.Headers.ContentLength is 0)
        {
            return null;
        }

        HlsStartResponse? startResponse;
        try
        {
            startResponse = await response.Content.ReadFromJsonAsync<HlsStartResponse>(
                JsonOptions,
                cancellationToken);
        }
        catch (JsonException)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(startResponse?.WorkerBaseUrl))
        {
            return null;
        }

        string value = startResponse.WorkerBaseUrl.EndsWith('/')
            ? startResponse.WorkerBaseUrl
            : $"{startResponse.WorkerBaseUrl}/";
        if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? address))
        {
            throw new LiveStreamException("WorkerBaseUrlIsInvalid");
        }

        if (address.Scheme is not ("http" or "https"))
        {
            throw new LiveStreamException("WorkerBaseUrlIsInvalid");
        }

        return address;
    }

    private static Uri ResolveWorkerRequestUri(SessionRecord session, string relativePath)
    {
        return session.WorkerBaseAddress is null
            ? new Uri(relativePath, UriKind.Relative)
            : new Uri(session.WorkerBaseAddress, relativePath);
    }

    private async Task<Uri> SelectWorkerAsync(CancellationToken cancellationToken) =>
        await workerSelector.SelectAsync(worker, cancellationToken)
        ?? throw new LiveStreamException("StreamProcessStartFailed");

    private sealed class SessionRecord
    {
        public SessionRecord(long streamId, long channelId, string channelName, int mode, long? videoFileId, DateTimeOffset startedAt)
        {
            StreamId = streamId;
            ChannelId = channelId;
            ChannelName = channelName;
            Mode = mode;
            VideoFileId = videoFileId;
            StartedAt = startedAt;
            LastKeepAt = startedAt;
        }

        public long StreamId { get; }

        public long ChannelId { get; }

        public string ChannelName { get; }

        public int Mode { get; }

        public long? VideoFileId { get; }

        public DateTimeOffset StartedAt { get; }

        public DateTimeOffset LastKeepAt { get; set; }

        public bool IsRunningCached { get; set; } = true;

        public Uri? WorkerBaseAddress { get; set; }

        /// <summary>ffmpeg-worker を介さない配信の種別。null なら HLS のセッション。</summary>
        public string? DirectType { get; set; }

        public string? Client { get; set; }
    }

    /// <summary>
    /// 中身を <see cref="HttpResponseMessage"/> の読み手へ委ね、読み終わったら応答ごと畳む。
    /// 読み手が破棄すれば worker への接続も切れ、worker 側はそれを合図に ffmpeg を止める。
    /// </summary>
    private sealed class ResponseOwningStream(HttpResponseMessage response, Stream inner) : Stream
    {
        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            inner.ReadAsync(buffer, cancellationToken);

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                inner.Dispose();
                response.Dispose();
            }

            base.Dispose(disposing);
        }

        public override async ValueTask DisposeAsync()
        {
            await inner.DisposeAsync();
            response.Dispose();
            await base.DisposeAsync();
        }
    }

    private sealed record HlsLiveStartRequest(long StreamId, long ChannelId, int Height, string VideoBitrate, string AudioBitrate, int SegmentSeconds, int? Priority, string? Command);

    private sealed record LowLatencyLiveStartRequest(long StreamId, long ChannelId, int Height, string VideoBitrate, string AudioBitrate, int? Priority);

    private sealed record HlsRecordedStartRequest(long StreamId, string Path, int Height, string VideoBitrate, string AudioBitrate, int SegmentSeconds, double PlayPosition, string Command, bool IsTransportStream);

    private sealed record HlsStartResponse(string? WorkerBaseUrl);

    private sealed record HlsStatusResponse(
        bool Found,
        bool IsRunning,
        string? LastError,
        string? RecentOutput);

    private sealed record TranscodeLiveRequest(long ChannelId, int Height, string VideoBitrate, string AudioBitrate, IReadOnlyList<string> FormatArguments, int? Priority, string? Command);

    private sealed record TranscodeRecordedRequest(string Path, int Height, string VideoBitrate, string AudioBitrate, IReadOnlyList<string> FormatArguments, double PlayPosition, string Command, bool IsTransportStream);

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

    [LoggerMessage(
        EventId = 2002,
        Level = LogLevel.Error,
        Message = "Could not fully clean up live stream {StreamId}")]
    private static partial void LogStreamCleanupFailed(
        ILogger logger,
        long streamId,
        Exception exception);
}
