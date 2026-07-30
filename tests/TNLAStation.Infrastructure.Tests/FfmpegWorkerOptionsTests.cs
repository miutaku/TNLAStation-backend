using TNLAStation.Infrastructure.Configuration;

namespace TNLAStation.Infrastructure.Tests;

public sealed class FfmpegWorkerOptionsTests
{
    [Fact]
    public void ResolveUrlsUsesTheBackwardCompatibleBaseUrlByDefault()
    {
        var options = new FfmpegWorkerOptions
        {
            BaseUrl = "http://worker/",
        };

        Assert.Equal("http://worker/", options.ResolveEncodeBaseUrl());
        Assert.Equal("http://worker/", options.ResolveStreamingBaseUrl());
    }

    [Fact]
    public void ResolveUrlsKeepsEncodeAndStreamingPoolsIndependent()
    {
        var options = new FfmpegWorkerOptions
        {
            BaseUrl = "http://fallback/",
            EncodeBaseUrl = "http://encode/",
            StreamingBaseUrl = "http://streaming/",
        };

        Assert.Equal("http://encode/", options.ResolveEncodeBaseUrl());
        Assert.Equal("http://streaming/", options.ResolveStreamingBaseUrl());
    }
}
