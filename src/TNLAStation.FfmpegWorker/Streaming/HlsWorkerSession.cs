using System.Diagnostics;
using TNLAStation.FfmpegWorker.Options;
using TNLAStation.FfmpegWorker.Processes;

namespace TNLAStation.FfmpegWorker.Streaming;

/// <summary>
/// HLS 配信 1 本の ffmpeg プロセス。受信元・ffmpeg・書き出したファイルが一組で寿命を共にする。
///
/// 受信元は 2 通りある。ライブはチューナーの流れを ffmpeg へ流し込むので <c>source</c> を
/// 持つ。録画済みは ffmpeg にファイルを直接読ませる。流し込むと頭出しができない。
/// </summary>
internal sealed class HlsWorkerSession(long streamId, FfmpegOptions options, Stream? source, ProcessCommand command, IAsyncDisposable processLease) : IAsyncDisposable
{
    private readonly CancellationTokenSource lifetime = new();
    private readonly List<string> errorOutput = [];
    private Process? ffmpeg;
    private Task? pump;
    private int disposed;

    public bool IsRunning => ffmpeg is { HasExited: false };

    public void Start()
    {
        var startInfo = new ProcessStartInfo(command.FileName)
        {
            RedirectStandardInput = source is not null,
            RedirectStandardError = true,
            RedirectStandardOutput = false,
            UseShellExecute = false,
        };
        foreach (string argument in command.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        ffmpeg = Process.Start(startInfo)
            ?? throw new InvalidOperationException("StreamProcessStartFailed");
        Process started = ffmpeg;
        try
        {
            started.ErrorDataReceived += OnErrorDataReceived;
            started.BeginErrorReadLine();
            pump = source is null ? null : Task.Run(() => PumpAsync(started), CancellationToken.None);
        }
        catch
        {
            _ = ChildProcessLifetime.StopAsync(started).AsTask().GetAwaiter().GetResult();
            started.ErrorDataReceived -= OnErrorDataReceived;
            started.Dispose();
            ffmpeg = null;
            DeleteStreamFiles();
            processLease.DisposeAsync().AsTask().GetAwaiter().GetResult();
            throw;
        }
    }

    /// <summary>直近の ffmpeg 標準エラー出力。プレイリストが出ないまま落ちた理由を伝える。</summary>
    public string DescribeFailure(string reason)
    {
        lock (errorOutput)
        {
            return errorOutput.Count == 0 ? reason : $"{reason}: {string.Join(" / ", errorOutput)}";
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) == 1)
        {
            return;
        }

        await lifetime.CancelAsync();
        try
        {
            await StopProcessAsync();
        }
        finally
        {
            try
            {
                // token を無視する入力でも、接続自体を閉じれば pump を戻せる。
                if (source is not null)
                {
                    await source.DisposeAsync();
                }
            }
            finally
            {
                try
                {
                    if (pump is not null)
                    {
                        await pump.WaitAsync(TimeSpan.FromSeconds(5));
                    }
                }
                catch (TimeoutException)
                {
                    // 受信が固まったまま返らないことがある。配信は既に止めたので待ち続けない。
                }
                finally
                {
                    lifetime.Dispose();
                    DeleteStreamFiles();
                    await processLease.DisposeAsync();
                }
            }
        }
    }

    private async Task PumpAsync(Process process)
    {
        try
        {
            await source!.CopyToAsync(process.StandardInput.BaseStream, lifetime.Token);
        }
        catch (OperationCanceledException)
        {
        }
        catch (IOException)
        {
        }
        finally
        {
            try
            {
                process.StandardInput.Close();
            }
            catch (IOException)
            {
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
            await ChildProcessLifetime.StopAsync(process);
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
        }
        catch (IOException)
        {
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
}
