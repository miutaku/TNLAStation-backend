using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TNLAStation.Application.Abstractions;
using TNLAStation.Application.Models;
using TNLAStation.Domain;
using TNLAStation.Infrastructure.Configuration;
using TNLAStation.Infrastructure.Configuration.EpgStation;
using TNLAStation.Infrastructure.Mirakurun;
using TNLAStation.Infrastructure.Streaming;

namespace TNLAStation.Infrastructure.Tests;

/// <summary>
/// IPTV や外部の client からの視聴も /api/streams に出さないと、誰がチューナーを掴んで
/// いるのか分からない。HLS のように開始要求を受けないので、配信中だけ list へ載せる。
/// </summary>
public sealed class DirectStreamTrackingTests
{
    [Fact]
    public async Task AStreamIsListedWhileItIsBeingReadAndGoesAwayWhenItCloses()
    {
        await using RemoteLiveStreamService service = Create();

        IAsyncDisposable tracked = await service.TrackDirectStreamAsync(
            new DirectStreamDescriptor("m2ts", 0, ChannelId: 3273601024, Client: "192.168.20.9 (PVR Live)"),
            CancellationToken.None);

        IReadOnlyList<StreamSession> listed = await service.ListAsync(CancellationToken.None);
        StreamSession session = Assert.Single(listed);
        Assert.Equal("LiveStream", session.Type);
        Assert.Equal(3273601024, session.ChannelId);
        Assert.Equal("192.168.20.9 (PVR Live)", session.Client);
        Assert.True(session.IsEnable);

        await tracked.DisposeAsync();

        Assert.Empty(await service.ListAsync(CancellationToken.None));
    }

    [Fact]
    public async Task ARecordedStreamIsReportedAsRecordedAndKeepsItsFile()
    {
        await using RemoteLiveStreamService service = Create();

        await using IAsyncDisposable tracked = await service.TrackDirectStreamAsync(
            new DirectStreamDescriptor("mp4", 1, VideoFileId: 42, Client: "10.0.0.2"),
            CancellationToken.None);

        StreamSession session = Assert.Single(await service.ListAsync(CancellationToken.None));
        Assert.Equal("RecordedStream", session.Type);
        Assert.Equal(42, session.VideoFileId);
        Assert.Equal(1, session.Mode);
    }

    /// <summary>畳み忘れの二重呼び出しで、他のセッションを巻き添えにしない。</summary>
    [Fact]
    public async Task ClosingTwiceRemovesOnlyItsOwnEntry()
    {
        await using RemoteLiveStreamService service = Create();
        IAsyncDisposable first = await service.TrackDirectStreamAsync(
            new DirectStreamDescriptor("m2ts", 0, ChannelId: 1), CancellationToken.None);
        await using IAsyncDisposable second = await service.TrackDirectStreamAsync(
            new DirectStreamDescriptor("m2ts", 0, ChannelId: 2), CancellationToken.None);

        await first.DisposeAsync();
        await first.DisposeAsync();

        StreamSession remaining = Assert.Single(await service.ListAsync(CancellationToken.None));
        Assert.Equal(2, remaining.ChannelId);
    }

    private static RemoteLiveStreamService Create() =>
        new(
            new HttpClient { BaseAddress = new Uri("http://worker.invalid/") },
            new StreamingWorkerSelector(Options.Create(new FfmpegWorkerOptions())),
            Unused.Instance,
            new EmptyEpg(),
            Unused.Instance,
            Options.Create(new StreamingOptions()),
            new EmptyConfig(),
            Options.Create(new MirakurunOptions()),
            TimeProvider.System,
            NullLogger<RemoteLiveStreamService>.Instance);




    /// <summary>番組表は空。放送中の番組が引けないときの見え方も、この試験で押さえる。</summary>
    private sealed class EmptyEpg : IEpgRepository
    {
        public ValueTask<IReadOnlyList<EpgChannel>> ListChannelsAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<EpgChannel>>([]);

        public ValueTask<EpgChannel?> GetChannelAsync(long channelId, CancellationToken cancellationToken) =>
            ValueTask.FromResult<EpgChannel?>(null);

        public ValueTask<EpgProgram?> GetProgramAsync(long programId, CancellationToken cancellationToken) =>
            ValueTask.FromResult<EpgProgram?>(null);

        public ValueTask<IReadOnlyList<EpgProgram>> FindProgramsByIdsAsync(
            IReadOnlyList<long> programIds,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<EpgProgram>>([]);

        public ValueTask<IReadOnlyList<EpgProgram>> FindProgramsAsync(
            EpgScheduleQuery query,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<EpgProgram>>([]);

        public ValueTask<IReadOnlyList<EpgProgram>> SearchProgramsAsync(
            EpgSearchQuery query,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<EpgProgram>>([]);
    }

    private sealed class EmptyConfig : IEpgStationConfigAccessor
    {
        public EpgStationConfigFile Current { get; } = new();

        public bool IsFromConfigFile => false;
    }
}
