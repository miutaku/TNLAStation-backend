using System.Diagnostics;
using Microsoft.Extensions.Options;
using TNLAStation.FfmpegWorker.Mirakurun;
using TNLAStation.FfmpegWorker.Options;
using TNLAStation.FfmpegWorker.Processes;

namespace TNLAStation.FfmpegWorker.Streaming;

/// <summary>
/// 変換しながら 1 本の流れとして配る。HLS と違って途中のファイルを作らないので、
/// 遅れは小さいが、途中から見ることも巻き戻すこともできない。
/// </summary>
public sealed class TranscodeStreamer(WorkerMirakurunClient mirakurun, IOptions<FfmpegOptions> options, ProcessGate gate)
{
    private readonly FfmpegOptions options = options.Value;

    public async Task<Stream> OpenLiveAsync(long channelId, int height, string videoBitrate, string audioBitrate, IReadOnlyList<string> formatArguments, int? priority, string? command, CancellationToken cancellationToken)
    {
        ProcessLease lease = await gate.AcquireAsync(ProcessPriority.Viewing, cancellationToken);
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

        try
        {
            ProcessCommand processCommand = command is null
                ? new ProcessCommand(options.FfmpegPath, HlsArguments.CreateTranscode("pipe:0", isLiveInput: true, height, videoBitrate, audioBitrate, formatArguments, playPosition: null))
                : EpgStationStreamCommand.Expand(command, options, "pipe:0", "pipe:1");
            ProcessOutputStream stream = Start(processCommand, source, lease);
            stream.StartPump();
            return stream;
        }
        catch
        {
            await source.DisposeAsync();
            await lease.DisposeAsync();
            throw;
        }
    }

    public async Task<Stream> OpenRecordedAsync(string path, int height, string videoBitrate, string audioBitrate, IReadOnlyList<string> formatArguments, double playPosition, string? command, bool isTransportStream)
    {
        ProcessLease lease = await gate.AcquireAsync(ProcessPriority.Viewing, CancellationToken.None);
        Stream? source = null;
        try
        {
            source = isTransportStream && command is not null ? File.OpenRead(path) : null;
            ProcessCommand processCommand = command is null
                ? new ProcessCommand(options.FfmpegPath, HlsArguments.CreateTranscode(path, isLiveInput: false, height, videoBitrate, audioBitrate, formatArguments, playPosition))
                : EpgStationStreamCommand.Expand(command, options, isTransportStream ? "pipe:0" : path, "pipe:1", playPosition: playPosition, transportStream: isTransportStream);
            return Start(processCommand, source, lease);
        }
        catch
        {
            if (source is not null)
            {
                await source.DisposeAsync();
            }

            await lease.DisposeAsync();
            throw;
        }
    }

    private static ProcessOutputStream Start(ProcessCommand command, Stream? source, IAsyncDisposable lease)
    {
        var startInfo = new ProcessStartInfo(command.FileName)
        {
            RedirectStandardInput = source is not null,
            RedirectStandardOutput = true,
            RedirectStandardError = false,
            UseShellExecute = false,
        };
        foreach (string argument in command.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("StreamProcessStartFailed");
        return new ProcessOutputStream(process, source, lease);
    }
}
