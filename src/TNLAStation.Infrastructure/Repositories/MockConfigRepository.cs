using TNLAStation.Application.Abstractions;
using TNLAStation.Domain;

namespace TNLAStation.Infrastructure.Repositories;

public sealed class MockConfigRepository : IConfigRepository
{
    private static readonly StationConfiguration Configuration = new(
        SocketIoPort: 8888,
        Broadcast: new BroadcastAvailability(Gr: true, Bs: true, Cs: true, Sky: false),
        RecordedDirectories: ["recorded"],
        EncodeModes: ["H.264"],
        UrlScheme: new UrlSchemeConfiguration(
            M2Ts: new UrlSchemeInfo(
                Ios: "vlc-x-callback://x-callback-url/stream?url=PROTOCOL%3A%2F%2FADDRESS",
                Android: "intent://ADDRESS#Intent;action=android.intent.action.VIEW;type=video/*;scheme=PROTOCOL;end"),
            Video: new UrlSchemeInfo(
                Ios: "infuse://x-callback-url/play?url=PROTOCOL://ADDRESS",
                Android: "intent://ADDRESS#Intent;action=android.intent.action.VIEW;type=video/*;scheme=PROTOCOL;end"),
            Download: new UrlSchemeInfo(
                Ios: "vlc-x-callback://x-callback-url/download?url=PROTOCOL%3A%2F%2FADDRESS&filename=FILENAME")),
        IsEnableTsLiveStream: false,
        IsEnableTsRecordedStream: false,
        IsEnableEncodedRecordedStream: false);

    public ValueTask<StationConfiguration> GetAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(Configuration);
    }
}
