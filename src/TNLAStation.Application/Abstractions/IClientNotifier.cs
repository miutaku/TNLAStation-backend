namespace TNLAStation.Application.Abstractions;

/// <summary>
/// 画面へ「状態が変わった」と知らせる。
///
/// EPGStation の <c>IPCServer.notifyClient</c> / <c>SocketIOManageModel</c> にあたる。呼ぶ場所は
/// <c>EPGStation/src/model/event/EventSetter.ts</c> が列挙している節目 (ルールの追加・更新・
/// 有効化・無効化・削除、予約更新、録画の準備開始/中止/失敗/開始/失敗/完了、サムネイルの
/// 追加と削除、録画の削除、動画ファイルの追加・サイズ更新・削除、録画済みの新規作成、
/// アップロード、tag の作成・更新・関連付け・削除、保護状態の変更) と、
/// <c>StreamManageModel</c> の配信開始・停止。
/// </summary>
public interface IClientNotifier
{
    /// <summary>EPGStation の <c>updateStatus</c>。200ms 分の呼び出しは 1 本にまとめられる。</summary>
    void NotifyClient();

    /// <summary>EPGStation の <c>updateEncode</c>。エンコードの進捗が動いたとき。</summary>
    void NotifyUpdateEncodeProgress();
}

/// <summary>知らせる先が無い構成 (試験や、socket.io を立てない構成) 用。</summary>
public sealed class NullClientNotifier : IClientNotifier
{
    public static NullClientNotifier Instance { get; } = new();

    public void NotifyClient()
    {
    }

    public void NotifyUpdateEncodeProgress()
    {
    }
}
