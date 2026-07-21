using Microsoft.Extensions.Options;
using TNLAStation.Application.Abstractions;
using TNLAStation.Infrastructure.Configuration;
using TNLAStation.Domain;

namespace TNLAStation.Infrastructure.Repositories;

/// <summary>
/// 画面へ渡す設定。保存先と変換の設定は実際の設定から作る。画面はここに並んだ選択肢しか
/// 出さないので、実体と食い違うと、選べるのに実行できない組み合わせが生まれる。
/// </summary>
public sealed class MockConfigRepository(
    IOptions<StorageOptions> storageOptions,
    IOptions<EncodeOptions> encodeOptions) : IConfigRepository
{
    private static readonly string[] DefaultEncodeModes = ["H.264"];

    private StationConfiguration Configuration => new(
        SocketIoPort: 8888,
        Broadcast: new BroadcastAvailability(Gr: true, Bs: true, Cs: true, Sky: false),
        RecordedDirectories: storageOptions.Value.RecordedDirectories.Count > 0
            ? [.. storageOptions.Value.RecordedDirectories.Select(directory => directory.Name)]
            : ["recorded"],
        EncodeModes: encodeOptions.Value.Modes.Count > 0
            ? [.. encodeOptions.Value.Modes.Select(mode => mode.Name)]
            : DefaultEncodeModes,
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
        IsEnableTsRecordedStream: true,
        IsEnableEncodedRecordedStream: true);

    public ValueTask<StationConfiguration> GetAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(Configuration);
    }
}
