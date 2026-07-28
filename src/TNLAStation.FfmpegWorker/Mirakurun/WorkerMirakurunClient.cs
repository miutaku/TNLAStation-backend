using Microsoft.Extensions.Options;
using TNLAStation.FfmpegWorker.Options;

namespace TNLAStation.FfmpegWorker.Mirakurun;

/// <summary>
/// ライブ視聴のためだけの最小 Mirakurun クライアント。番組表同期は backend 側が持つので、
/// worker はチューナーの MPEG-TS を開く 1 メソッドだけを知っていればよい。
/// </summary>
public sealed class WorkerMirakurunClient(HttpClient httpClient)
{
    /// <summary>
    /// 放送中の MPEG-TS を開く。呼び出し側が閉じるまでチューナーを占有し続けるので、
    /// 返ってきた <see cref="Stream"/> は必ず破棄する。
    /// </summary>
    public async ValueTask<Stream> OpenServiceStreamAsync(long channelId, CancellationToken cancellationToken, int? priority = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"api/services/{channelId}/stream?decode=1");
        if (priority is { } value)
        {
            request.Headers.Add("X-Mirakurun-Priority", value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        HttpResponseMessage response = await httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        try
        {
            response.EnsureSuccessStatusCode();
            Stream content = await response.Content.ReadAsStreamAsync(cancellationToken);
            return new ResponseOwningStream(response, content);
        }
        catch
        {
            response.Dispose();
            throw;
        }
    }

    /// <summary>
    /// 中身を <see cref="HttpResponseMessage"/> の読み手へ委ね、読み終わったら応答ごと畳む。
    /// 応答を先に破棄すると、まだ読んでいる接続を切ってしまう。
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
}
