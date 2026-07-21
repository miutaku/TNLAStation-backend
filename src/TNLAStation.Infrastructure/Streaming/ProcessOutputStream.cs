using System.Diagnostics;

namespace TNLAStation.Infrastructure.Streaming;

/// <summary>
/// ffmpeg の標準出力を、そのまま読める 1 本の流れとして扱う。
///
/// 読み手が閉じたら process も受信元も畳む。閉じ忘れると、誰も読んでいない変換が回り続け、
/// チューナーも CPU も掴んだままになる。
/// </summary>
internal sealed class ProcessOutputStream(Process process, Stream? source) : Stream
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
                    // 視聴の終了。
                }
                catch (IOException)
                {
                    // 受信が切れた。読み手には、そこまで届いた分で終わりとして見える。
                }
                finally
                {
                    try
                    {
                        process.StandardInput.Close();
                    }
                    catch (IOException)
                    {
                        // ffmpeg が先に終わっていれば閉じる相手がいない。
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
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
            // 既に終わって回収済み。
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

        if (source is not null)
        {
            await source.DisposeAsync();
        }

        process.Dispose();
        lifetime.Dispose();
        await base.DisposeAsync();
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
