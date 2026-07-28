using System.Diagnostics;

namespace TNLAStation.FfmpegWorker.Processes;

/// <summary>
/// ffmpeg の標準出力を、そのまま読める 1 本の流れとして扱う。
///
/// 読み手が閉じたら process も受信元も畳む。閉じ忘れると、誰も読んでいない変換が回り続け、
/// チューナーも CPU も掴んだままになる。
/// </summary>
internal sealed class ProcessOutputStream(Process process, Stream? source, IAsyncDisposable processLease) : Stream
{
    private readonly CancellationTokenSource lifetime = new();
    private Task? pump;
    private int disposed;

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    /// <summary>
    /// 受信元を ffmpeg の標準入力へ流し始める。ファイルを直接読ませる場合は要らない。
    /// </summary>
    public void StartPump()
    {
        if (source is null)
        {
            return;
        }

        pump = Task.Run(
            async () =>
            {
                try
                {
                    await source.CopyToAsync(process.StandardInput.BaseStream, lifetime.Token);
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
            },
            CancellationToken.None);
    }

    public override int Read(byte[] buffer, int offset, int count) =>
        process.StandardOutput.BaseStream.Read(buffer, offset, count);

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
        process.StandardOutput.BaseStream.ReadAsync(buffer, cancellationToken);

    public override Task<int> ReadAsync(
        byte[] buffer,
        int offset,
        int count,
        CancellationToken cancellationToken) =>
        process.StandardOutput.BaseStream.ReadAsync(buffer, offset, count, cancellationToken);

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    public override async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) == 1)
        {
            return;
        }

        await lifetime.CancelAsync();
        bool stopped = false;
        try
        {
            stopped = await ChildProcessLifetime.StopAsync(process);

            // 先に入力元を閉じる。CancellationToken を無視して Read が固まる実装でも、
            // 接続を閉じることで pump を戻せる。
            if (source is not null)
            {
                await source.DisposeAsync();
            }

            if (pump is not null)
            {
                try
                {
                    await pump.WaitAsync(TimeSpan.FromSeconds(5));
                }
                catch (TimeoutException)
                {
                    // 受信が固まったまま返らないことがある。変換は既に止めた。
                }
            }
        }
        finally
        {
            process.Dispose();
            lifetime.Dispose();
            await processLease.DisposeAsync();
            await base.DisposeAsync();
        }

        if (!stopped)
        {
            throw new InvalidOperationException("StreamProcessDidNotExit");
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            DisposeAsync().AsTask().GetAwaiter().GetResult();
        }

        base.Dispose(disposing);
    }
}
