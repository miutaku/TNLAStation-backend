using System.Diagnostics;
using Microsoft.Extensions.Logging;
using TNLAStation.Application.Abstractions;
using TNLAStation.Infrastructure.Configuration;

namespace TNLAStation.Infrastructure.Streaming;

/// <summary>
/// HLS 配信 1 本。受信元、ffmpeg、書き出したファイルが一組で寿命を共にする。どれか 1 つ
/// でも残すとチューナーかディスクが解放されない。
///
/// 受信元は 2 通りある。ライブはチューナーの流れを ffmpeg へ流し込むので <c>source</c> を
/// 持つ。録画済みは ffmpeg にファイルを直接読ませる。流し込むと頭出しができない。
/// </summary>
internal sealed partial class HlsStreamSession(
    long streamId,
    long channelId,
    string channelName,
    int mode,
    DateTimeOffset startedAt,
    StreamingOptions options,
    Stream? source,
    string[] arguments,
    ILogger logger) : IAsyncDisposable
{
    private readonly CancellationTokenSource lifetime = new();
    private readonly List<string> errorOutput = [];
    private Process? ffmpeg;
    private Task? pump;
    private int disposed;

    public long StreamId => streamId;

    public long ChannelId => channelId;

    public string ChannelName => channelName;

    public int Mode => mode;

    public DateTimeOffset StartedAt { get; } = startedAt;

    public DateTimeOffset LastKeepAt { get; set; } = startedAt;

    /// <summary>録画済みの再生なら、その動画ファイル。ライブなら null。</summary>
    public long? VideoFileId { get; init; }

    public bool IsRunning => ffmpeg is { HasExited: false };

    private string PlaylistPath => Path.Combine(options.WorkDirectory, $"stream{streamId}.m3u8");

    public void Start()
    {
        var startInfo = new ProcessStartInfo(options.FfmpegPath)
        {
            RedirectStandardInput = source is not null,
            RedirectStandardError = true,
            RedirectStandardOutput = false,
            UseShellExecute = false,
        };
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        ffmpeg = Process.Start(startInfo)
            ?? throw new LiveStreamException("StreamProcessStartFailed");
        ffmpeg.ErrorDataReceived += OnErrorDataReceived;
        ffmpeg.BeginErrorReadLine();
        pump = source is null ? null : Task.Run(PumpAsync, CancellationToken.None);
    }

    /// <summary>
    /// プレイリストが書かれるまで待つ。ここで待たずに stream id を返すと、画面は
    /// まだ存在しない .m3u8 を取りに行って再生に失敗する。
    /// </summary>
    public async Task WaitForPlaylistAsync(CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetime.Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));

        try
        {
            while (!File.Exists(PlaylistPath))
            {
                if (ffmpeg is { HasExited: true })
                {
                    throw new LiveStreamException(DescribeFailure("ffmpeg exited before producing a playlist"));
                }

                await Task.Delay(TimeSpan.FromMilliseconds(200), timeout.Token);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new LiveStreamException(DescribeFailure("ffmpeg produced no playlist in time"));
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) == 1)
        {
            return;
        }

        await lifetime.CancelAsync();
        await StopProcessAsync();

        if (pump is not null)
        {
            try
            {
                await pump.WaitAsync(TimeSpan.FromSeconds(5));
            }
            catch (TimeoutException)
            {
                // 受信が固まったまま返らないことがある。配信は既に止めたので待ち続けない。
            }
        }

        if (source is not null)
        {
            await source.DisposeAsync();
        }

        lifetime.Dispose();
        DeleteStreamFiles();
    }

    private async Task PumpAsync()
    {
        try
        {
            await source!.CopyToAsync(ffmpeg!.StandardInput.BaseStream, lifetime.Token);
        }
        catch (OperationCanceledException)
        {
            // 視聴の終了。
        }
        catch (IOException exception)
        {
            LogPumpFailed(logger, streamId, exception);
        }
        finally
        {
            try
            {
                ffmpeg!.StandardInput.Close();
            }
            catch (IOException)
            {
                // ffmpeg が先に終わっていれば閉じる相手がいない。
            }
        }
    }

    private async Task StopProcessAsync()
    {
        Process? process = ffmpeg;
        if (process is null)
        {
            return;
        }

        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
            }
        }
        catch (InvalidOperationException)
        {
            // 既に終了して回収済み。
        }
        catch (TimeoutException)
        {
            LogProcessDidNotExit(logger, streamId);
        }
        finally
        {
            process.ErrorDataReceived -= OnErrorDataReceived;
            process.Dispose();
            ffmpeg = null;
        }
    }

    private void DeleteStreamFiles()
    {
        try
        {
            foreach (string path in Directory.EnumerateFiles(options.WorkDirectory, $"stream{streamId}*"))
            {
                File.Delete(path);
            }
        }
        catch (DirectoryNotFoundException)
        {
            // 置き場ごと無ければ消すものもない。
        }
        catch (IOException exception)
        {
            LogCleanupFailed(logger, streamId, exception);
        }
    }

    private void OnErrorDataReceived(object sender, DataReceivedEventArgs args)
    {
        if (args.Data is null)
        {
            return;
        }

        // 失敗の理由を伝えるために直近だけ残す。全部ためると長時間の視聴で膨らむ。
        lock (errorOutput)
        {
            errorOutput.Add(args.Data);
            if (errorOutput.Count > 20)
            {
                errorOutput.RemoveAt(0);
            }
        }
    }

    private string DescribeFailure(string reason)
    {
        lock (errorOutput)
        {
            return errorOutput.Count == 0 ? reason : $"{reason}: {string.Join(" / ", errorOutput)}";
        }
    }

    [LoggerMessage(
        EventId = 2010,
        Level = LogLevel.Warning,
        Message = "Live stream {StreamId} lost its Mirakurun feed")]
    private static partial void LogPumpFailed(ILogger logger, long streamId, Exception exception);

    [LoggerMessage(
        EventId = 2011,
        Level = LogLevel.Warning,
        Message = "ffmpeg for live stream {StreamId} did not exit after being killed")]
    private static partial void LogProcessDidNotExit(ILogger logger, long streamId);

    [LoggerMessage(
        EventId = 2012,
        Level = LogLevel.Warning,
        Message = "Could not delete the stream files of live stream {StreamId}")]
    private static partial void LogCleanupFailed(ILogger logger, long streamId, Exception exception);
}
