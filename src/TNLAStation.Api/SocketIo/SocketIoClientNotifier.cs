using TNLAStation.Application.Abstractions;

namespace TNLAStation.Api.SocketIo;

/// <summary>状態変更の通知を socket.io へ流す。</summary>
public sealed class SocketIoClientNotifier(SocketIoHub hub) : IClientNotifier
{
    public void NotifyClient() => hub.NotifyClient();

    public void NotifyUpdateEncodeProgress() => hub.NotifyUpdateEncodeProgress();
}

/// <summary>
/// 状態を変える HTTP 要求が成功したあとに <c>updateStatus</c> を流す。
///
/// EPGStation は各操作の中から <c>ipc.notifyClient()</c> を呼ぶ (EventSetter が繋いでいる)。
/// 対象は下の表のとおりで、EPGStation が鳴らさない操作 (配信の keep、参照系) は含めない。
/// 失敗した要求では鳴らさない — EPGStation も、イベントを発火する処理まで到達しなければ鳴らない。
/// </summary>
internal sealed class SocketIoNotifyMiddleware(RequestDelegate next)
{
    /// <summary>
    /// <c>(method, パスの正規化形)</c>。<c>{n}</c> は数値の経路変数。
    /// 根拠は EventSetter.ts と StreamManageModel.ts の <c>notifyClient()</c> 呼び出し位置。
    /// </summary>
    private static readonly HashSet<string> NotifyingRoutes = new(StringComparer.Ordinal)
    {
        "POST /api/rules",
        "POST /api/rules/keyword",
        "PUT /api/rules/{n}",
        "DELETE /api/rules/{n}",
        "PUT /api/rules/{n}/enable",
        "PUT /api/rules/{n}/disable",

        "POST /api/reserves",
        "PUT /api/reserves/{n}",
        "DELETE /api/reserves/{n}",
        "DELETE /api/reserves/{n}/skip",
        "DELETE /api/reserves/{n}/overlap",
        "POST /api/reserves/update",

        // 録画済み: 新規作成・削除・保護状態の変更・クリーンアップ
        "POST /api/recorded",
        "DELETE /api/recorded/{n}",
        "PUT /api/recorded/{n}/protect",
        "PUT /api/recorded/{n}/unprotect",
        "POST /api/recorded/cleanup",

        // 動画ファイル: アップロード・削除
        "POST /api/videos/upload",
        "DELETE /api/videos/{n}",

        "POST /api/thumbnails",
        "POST /api/thumbnails/videos/{n}",
        "DELETE /api/thumbnails/{n}",
        "POST /api/thumbnails/cleanup",

        // tag: 作成・更新・削除・関連付けと解除
        "POST /api/tags",
        "PUT /api/tags/{n}",
        "DELETE /api/tags/{n}",
        "PUT /api/tags/{n}/relate",
        "DELETE /api/tags/{n}/relate",

        // 配信: 開始と停止 (StreamManageModel.start / stop)
        "DELETE /api/streams",
        "DELETE /api/streams/{n}",
        "GET /api/streams/live/{n}/hls",
        "GET /api/streams/recorded/{n}/hls",
    };

    public async Task InvokeAsync(HttpContext context, IClientNotifier notifier)
    {
        await next(context);

        if (context.Response.StatusCode is < 200 or >= 300)
        {
            return;
        }

        if (NotifyingRoutes.Contains($"{context.Request.Method} {Normalise(context.Request.Path)}"))
        {
            notifier.NotifyClient();
        }
    }

    /// <summary>経路変数の数値部分を <c>{n}</c> に畳む。</summary>
    internal static string Normalise(PathString path)
    {
        string value = path.Value ?? string.Empty;
        string[] segments = value.Split('/');
        for (int i = 0; i < segments.Length; i++)
        {
            if (segments[i].Length > 0 && segments[i].All(char.IsAsciiDigit))
            {
                segments[i] = "{n}";
            }
        }

        string joined = string.Join('/', segments);
        return joined.Length > 1 ? joined.TrimEnd('/') : joined;
    }
}
