namespace TNLAStation.Api.Middleware;

/// <summary>
/// HEAD を GET と同じ経路で処理し、本文だけ捨てる。
///
/// EPGStation は Express なので、GET を定義すると HEAD にも自動で応じる。ASP.NET は
/// MapGet だけでは HEAD に 405 を返す。取り込む前に HEAD で確かめる client がありうる。
/// そこで諦められると番組情報が付かない可能性がある。
/// </summary>
public sealed class HeadRequestMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (!HttpMethods.IsHead(context.Request.Method))
        {
            await next(context);
            return;
        }

        context.Request.Method = HttpMethods.Get;
        Stream original = context.Response.Body;
        var counter = new CountingStream();
        context.Response.Body = counter;
        try
        {
            await next(context);
        }
        finally
        {
            context.Response.Body = original;
            context.Request.Method = HttpMethods.Head;
            // 本文を書かないので、長さは数えた分を自分で載せる。
            if (!context.Response.HasStarted)
            {
                context.Response.ContentLength = counter.Length;
            }
        }
    }

    /// <summary>書かれた長さだけ数えて捨てる。4MB の番組表を HEAD のために持たない。</summary>
    private sealed class CountingStream : Stream
    {
        private long written;

        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => written;

        public override long Position
        {
            get => written;
            set => throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count) => written += count;

        public override void Write(ReadOnlySpan<byte> buffer) => written += buffer.Length;

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            written += count;
            return Task.CompletedTask;
        }

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            written += buffer.Length;
            return ValueTask.CompletedTask;
        }

        public override void Flush()
        {
        }

        public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
