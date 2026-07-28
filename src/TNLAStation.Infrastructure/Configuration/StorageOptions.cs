namespace TNLAStation.Infrastructure.Configuration;

public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    /// <summary>
    /// Recording destinations, in the order EPGStation lists them under <c>config.recorded</c>.
    /// The default stays empty because the configuration binder appends to, rather than replaces,
    /// a pre-populated collection, which would leave a phantom directory in every deployment.
    /// </summary>
    public IReadOnlyList<RecordedDirectoryOptions> RecordedDirectories { get; init; } = [];

    /// <summary>
    /// 各保存先の空き容量を確認する間隔。<see cref="RecordedDirectoryOptions.LimitThresholdMb"/>
    /// を設定した保存先が 1 つも無ければ、確認自体をしない。
    /// </summary>
    public int StorageLimitCheckIntervalSeconds { get; init; } = 60;

    /// <summary>
    /// 動画アップロード時、受信中だけ使う一時保存先 (EPGStation の uploadTempDir 相当)。
    /// 設定すると、ここへ書きながら受け取り、完了後に最終保存先へ移す。未設定なら最終保存先へ
    /// 直接書く — 途中で切断されると、そこまで受け取った分が中途半端なファイルとして残る。
    /// </summary>
    public string? UploadTempDirectory { get; init; }
}

public sealed class RecordedDirectoryOptions
{
    public string Name { get; init; } = string.Empty;

    public string Path { get; init; } = string.Empty;

    /// <summary>
    /// 空き容量がこの値 (MB) を下回ったら <see cref="Action"/> を行う。未設定ならこの保存先は
    /// 自動削除の対象にしない。
    /// </summary>
    public long? LimitThresholdMb { get; init; }

    /// <summary>
    /// 閾値を下回ったときの動作。<c>"remove"</c> で保護されていない最も古い録画から順に消す。
    /// それ以外 (未設定含む) なら <see cref="LimitCmd"/> の実行だけ行い、削除はしない。
    /// </summary>
    public string? Action { get; init; }

    /// <summary>
    /// 閾値を下回ったときに実行する外部コマンド。通知用途などを想定し、環境変数はそのまま継承する。
    /// </summary>
    public string? LimitCmd { get; init; }
}
