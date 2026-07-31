using System.Collections.Concurrent;
using System.Globalization;
using Microsoft.Extensions.Options;
using TNLAStation.FfmpegWorker.Mirakurun;
using TNLAStation.FfmpegWorker.Options;
using TNLAStation.FfmpegWorker.Processes;

namespace TNLAStation.FfmpegWorker.Streaming;

/// <summary>
/// backend から見た HLS 配信の制御面。streamId は backend 側が採番するので、ここでは
/// 言われた id で ffmpeg プロセスを起こし、言われた id で畳むだけの単純な登録簿にする。
/// </summary>
public sealed class HlsSessionRegistry(
    WorkerMirakurunClient mirakurun,
    IOptions<FfmpegOptions> options,
    ProcessGate gate)
{
    private readonly ConcurrentDictionary<long, HlsWorkerSession> sessions = new();
    private readonly FfmpegOptions options = options.Value;

    public async Task StartLiveAsync(long streamId, long channelId, int height, string videoBitrate, string audioBitrate, int segmentSeconds, int? priority, string? command, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(options.WorkDirectory);
        ProcessCommand processCommand = command is null
            ? new ProcessCommand(options.FfmpegPath, HlsArguments.CreateLive(options.WorkDirectory, streamId, height, videoBitrate, audioBitrate, segmentSeconds))
            : EpgStationStreamCommand.Expand(command, options, "pipe:0", Playlist(streamId), streamId);
        await StartFromTunerAsync(streamId, channelId, priority, processCommand, cancellationToken);
    }

    /// <summary>ファイルは書かず、配信サーバーへ送り込む。</summary>
    public async Task StartLowLatencyAsync(long streamId, long channelId, int height, string videoBitrate, string audioBitrate, int? priority, CancellationToken cancellationToken)
    {
        if (options.LowLatencyPublishUrlTemplate is not { Length: > 0 } template)
        {
            throw new InvalidOperationException("Ffmpeg:LowLatencyPublishUrlTemplate is not configured.");
        }

        string publishUrl = template.Replace("{streamId}", streamId.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);
        var processCommand = new ProcessCommand(options.FfmpegPath, HlsArguments.CreateLowLatency(publishUrl, height, videoBitrate, audioBitrate));
        await StartFromTunerAsync(streamId, channelId, priority, processCommand, cancellationToken);
    }

    private async Task StartFromTunerAsync(long streamId, long channelId, int? priority, ProcessCommand command, CancellationToken cancellationToken)
    {
        IAsyncDisposable lease = await gate.AcquireAsync(cancellationToken);
        Stream source;
        try
        {
            source = await mirakurun.OpenServiceStreamAsync(channelId, cancellationToken, priority);
        }
        catch
        {
            await lease.DisposeAsync();
            throw;
        }

        HlsWorkerSession? session = null;
        try
        {
            session = new HlsWorkerSession(streamId, options, source, command, lease);
            session.Start();
        }
        catch
        {
            await source.DisposeAsync();
            if (session is null)
            {
                await lease.DisposeAsync();
            }

            throw;
        }

        if (!sessions.TryAdd(streamId, session))
        {
            await session.DisposeAsync();
            throw new InvalidOperationException($"Stream {streamId} is already registered.");
        }
    }

    public async Task StartRecordedAsync(long streamId, string path, int height, string videoBitrate, string audioBitrate, int segmentSeconds, double playPosition, string? command, bool isTransportStream)
    {
        Directory.CreateDirectory(options.WorkDirectory);
        IAsyncDisposable lease = await gate.AcquireAsync(CancellationToken.None);
        Stream? source = null;
        HlsWorkerSession? session = null;
        try
        {
            source = isTransportStream && command is not null ? File.OpenRead(path) : null;
            ProcessCommand processCommand = command is null
                ? new ProcessCommand(options.FfmpegPath, HlsArguments.CreateRecorded(options.WorkDirectory, streamId, height, videoBitrate, audioBitrate, segmentSeconds, path, playPosition))
                : EpgStationStreamCommand.Expand(command, options, isTransportStream ? "pipe:0" : path, Playlist(streamId), streamId, playPosition, isTransportStream);
            session = new HlsWorkerSession(streamId, options, source, processCommand, lease);
            session.Start();
        }
        catch
        {
            if (source is not null)
            {
                await source.DisposeAsync();
            }

            if (session is null)
            {
                await lease.DisposeAsync();
            }

            throw;
        }

        if (!sessions.TryAdd(streamId, session))
        {
            await session.DisposeAsync();
            throw new InvalidOperationException($"Stream {streamId} is already registered.");
        }
    }

    private string Playlist(long streamId) => Path.Combine(options.WorkDirectory, $"stream{streamId}.m3u8");

    public (bool Found, bool IsRunning, string? LastError, string? RecentOutput) GetStatus(long streamId)
    {
        if (!sessions.TryGetValue(streamId, out HlsWorkerSession? session))
        {
            return (false, false, null, null);
        }

        bool running = session.IsRunning;
        return (
            true,
            running,
            running ? null : session.DescribeFailure("ffmpeg exited"),
            session.RecentOutput);
    }

    public async ValueTask<bool> StopAsync(long streamId)
    {
        if (!sessions.TryRemove(streamId, out HlsWorkerSession? session))
        {
            return false;
        }

        await session.DisposeAsync();
        return true;
    }
}
