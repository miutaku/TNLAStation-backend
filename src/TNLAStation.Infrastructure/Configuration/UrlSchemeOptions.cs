namespace TNLAStation.Infrastructure.Configuration;

/// <summary>
/// 視聴・ダウンロード用の URL Scheme (EPGStation の urlscheme 相当)。画面はこれを使って
/// 外部プレイヤーを呼び出すリンクを組み立てる。既定値は EPGStation の doc の既定と揃えてある。
/// </summary>
public sealed class UrlSchemeOptions
{
    public const string SectionName = "UrlScheme";

    public UrlSchemeEntryOptions M2Ts { get; init; } = new()
    {
        Ios = "vlc-x-callback://x-callback-url/stream?url=PROTOCOL%3A%2F%2FADDRESS\"",
        Android = "intent://ADDRESS#Intent;action=android.intent.action.VIEW;type=video/*;scheme=PROTOCOL;end",
    };

    public UrlSchemeEntryOptions Video { get; init; } = new()
    {
        Ios = "infuse://x-callback-url/play?url=PROTOCOL://ADDRESS",
        Android = "intent://ADDRESS#Intent;action=android.intent.action.VIEW;type=video/*;scheme=PROTOCOL;end",
    };

    public UrlSchemeEntryOptions Download { get; init; } = new()
    {
        Ios = "vlc-x-callback://x-callback-url/stream?url=PROTOCOL%3A%2F%2FADDRESS&filename=FILENAME",
    };
}

public sealed class UrlSchemeEntryOptions
{
    public string? Ios { get; init; }

    public string? Android { get; init; }

    public string? Mac { get; init; }

    public string? Win { get; init; }
}
