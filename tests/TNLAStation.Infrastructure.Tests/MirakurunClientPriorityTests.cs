using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TNLAStation.Infrastructure.Mirakurun;

namespace TNLAStation.Infrastructure.Tests;

public sealed class MirakurunClientPriorityTests
{
    [Fact]
    public async Task OpenServiceStreamAsyncSendsThePriorityHeaderWhenGiven()
    {
        var capture = new CapturingHandler();
        using var httpClient = new HttpClient(capture) { BaseAddress = new Uri("http://mirakurun.example/") };
        var client = new MirakurunClient(
            httpClient,
            Options.Create(new MirakurunOptions { BaseUrl = "http://mirakurun.example/" }),
            NullLogger<MirakurunClient>.Instance);

        await using Stream stream = await client.OpenServiceStreamAsync(1, CancellationToken.None, priority: 2);
        await stream.DisposeAsync();

        Assert.NotNull(capture.LastRequest);
        Assert.True(capture.LastRequest!.Headers.TryGetValues("X-Mirakurun-Priority", out IEnumerable<string>? values));
        Assert.Equal("2", Assert.Single(values!));
    }

    [Fact]
    public async Task OpenServiceStreamAsyncOmitsThePriorityHeaderForPlainViewing()
    {
        var capture = new CapturingHandler();
        using var httpClient = new HttpClient(capture) { BaseAddress = new Uri("http://mirakurun.example/") };
        var client = new MirakurunClient(
            httpClient,
            Options.Create(new MirakurunOptions { BaseUrl = "http://mirakurun.example/" }),
            NullLogger<MirakurunClient>.Instance);

        await using Stream stream = await client.OpenServiceStreamAsync(1, CancellationToken.None);
        await stream.DisposeAsync();

        Assert.NotNull(capture.LastRequest);
        Assert.False(capture.LastRequest!.Headers.Contains("X-Mirakurun-Priority"));
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastRequest = request;
            var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StreamContent(new MemoryStream()),
            };
            return Task.FromResult(response);
        }
    }
}
