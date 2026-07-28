using Microsoft.Extensions.Configuration;

namespace TNLAStation.Infrastructure.DependencyInjection;

/// <summary>
/// 起動に要る外部依存が揃っているかを見て、揃っていなければ止める。
///
/// EPGStation は縮退運転をしない。config.yml が読めなければ
/// <c>Configuration.readConfig</c> が fatal を出して <c>process.exit(1)</c> し、Mirakurun と
/// データベースは <c>ConnectionCheckModel.checkMirakurun</c> / <c>checkDB</c> が繋がるまで
/// 1 秒間隔で待ち続ける。繋がらないまま先へ進む経路が無い。
///
/// TNLAStation は契約試験を外部サービス無しで回すために、Mirakurun やデータベースが
/// 無い構成では機能ごとに「何もしない実装」へ差し替えられるようにしてある。これは上流に
/// 対応物が無い仕組みなので、既定で有効にしてはいけない。既定で有効だと、設定の書き漏らしが
/// 起動失敗ではなく「一部の機能だけ静かに動かない」という形で現れ、原因に辿り着けない。
///
/// 実際に、Mirakurun 未設定が「放送局ロゴが出ない」という見た目の問題としてだけ表面化し、
/// 同じ原因で EPG 更新・予約生成・録画・ライブ視聴が丸ごと止まっていたことがある。
/// ロゴの取得口だけが差し替え後も 200 を返していたためで、他は誰にも気付かれなかった。
/// </summary>
public static class StartupRequirements
{
    /// <summary>
    /// 外部サービス抜きで起動してよいか。既定は false (揃っていなければ起動しない)。
    /// 契約試験など、外部サービスを持たないまま HTTP の面だけ確かめたい構成でだけ true にする。
    /// </summary>
    public const string AllowDegradedStartupKey = "AllowDegradedStartup";

    public static bool IsDegradedStartupAllowed(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return configuration.GetValue(AllowDegradedStartupKey, defaultValue: false);
    }

    /// <summary>
    /// 足りない依存を全部集めて、1 度に報告する。1 つずつ直させると、直すたびに次の失敗が
    /// 出てきて何往復もすることになる。
    /// </summary>
    public static void Validate(IConfiguration configuration, string? loadedConfigFilePath)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        if (IsDegradedStartupAllowed(configuration))
        {
            return;
        }

        List<string> problems = [];

        if (string.IsNullOrWhiteSpace(configuration["Mirakurun:BaseUrl"]))
        {
            problems.Add(
                "Mirakurun の接続先が設定されていません。" +
                "config.yml の mirakurunPath (例: mirakurunPath: http://192.168.0.2:40772)、" +
                "または環境変数 Mirakurun__BaseUrl を設定してください。" +
                "未設定のままだと EPG 更新・予約生成・録画・ライブ視聴・放送局ロゴが全て動きません。");
        }

        if (string.IsNullOrWhiteSpace(configuration.GetConnectionString(
            InfrastructureServiceCollectionExtensions.PostgresConnectionName)))
        {
            problems.Add(
                "PostgreSQL の接続先が設定されていません。" +
                "環境変数 ConnectionStrings__PostgreSQL を設定してください。" +
                "未設定のままだと予約・録画・タグ・エンコードが一切保存されません。");
        }

        if (problems.Count == 0)
        {
            return;
        }

        string where = loadedConfigFilePath is null
            ? "EPGStation 形式の config.yml が見つかりませんでした。" +
              "既定の探索先は <ContentRoot>/config/config.yml で、環境変数 EPGSTATION_CONFIG でも指定できます。"
            : $"読み込んだ設定ファイル: {loadedConfigFilePath}";

        throw new StartupRequirementException(
            $"起動に必要な設定が足りません。{Environment.NewLine}" +
            string.Join(Environment.NewLine, problems.Select(problem => $"  - {problem}")) +
            Environment.NewLine +
            $"  {where}{Environment.NewLine}" +
            $"  外部サービス無しで起動してよい構成 (契約試験など) では " +
            $"{AllowDegradedStartupKey}=true を設定してください。");
    }
}

/// <summary>起動に要る設定が足りない。上流の fatal + exit(1) にあたる。</summary>
public sealed class StartupRequirementException(string message) : Exception(message);
