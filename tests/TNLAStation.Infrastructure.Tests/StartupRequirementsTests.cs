using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TNLAStation.Infrastructure.Configuration.EpgStation;
using TNLAStation.Infrastructure.DependencyInjection;

namespace TNLAStation.Infrastructure.Tests;

/// <summary>
/// 設定が足りないまま「静かに一部だけ動かない」状態で起動しないことを固定する。
///
/// EPGStation は縮退運転をしない。config.yml が読めなければ
/// <c>Configuration.readConfig</c> が fatal を出して <c>process.exit(1)</c> し、Mirakurun と
/// データベースは <c>ConnectionCheckModel</c> が繋がるまで待ち続ける。
///
/// この試験群が守っているのは、実際に起きた事故そのもの: config.yml を置き忘れたまま
/// 起動できてしまい、EPG 更新・予約生成・録画・ライブ視聴が丸ごと止まっているのに、
/// 症状としては「放送局ロゴが出ない」しか見えなかった。
/// </summary>
public sealed class StartupRequirementsTests
{
    private const string PostgresConnection =
        "Host=localhost;Port=5432;Database=tnlastation;Username=tnlastation;Password=x";

    private static IConfiguration Build(params (string Key, string? Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.ToDictionary(item => item.Key, item => item.Value))
            .Build();

    // ------------------------------------------------------------------ 起動を止める

    [Fact]
    public void WithoutMirakurunTheStartupStopsInsteadOfSilentlyDisablingHalfTheFeatures()
    {
        IConfiguration configuration = Build(("ConnectionStrings:PostgreSQL", PostgresConnection));

        var error = Assert.Throws<StartupRequirementException>(
            () => StartupRequirements.Validate(configuration, loadedConfigFilePath: null));

        Assert.Contains("mirakurunPath", error.Message, StringComparison.Ordinal);
        // 何が動かなくなるのかまで書いていないと、設定漏れに気付いても影響範囲が分からない。
        Assert.Contains("EPG 更新", error.Message, StringComparison.Ordinal);
        Assert.Contains("放送局ロゴ", error.Message, StringComparison.Ordinal);
        // config.yml が見つからなかったことと、探索先も案内する。
        Assert.Contains("EPGSTATION_CONFIG", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void WithoutADatabaseTheStartupStops()
    {
        IConfiguration configuration = Build(("Mirakurun:BaseUrl", "http://192.168.0.2:40772"));

        var error = Assert.Throws<StartupRequirementException>(
            () => StartupRequirements.Validate(configuration, loadedConfigFilePath: "/etc/config.yml"));

        Assert.Contains("ConnectionStrings__PostgreSQL", error.Message, StringComparison.Ordinal);
        Assert.Contains("/etc/config.yml", error.Message, StringComparison.Ordinal);
    }

    /// <summary>足りないものは 1 度に全部出す。1 つずつだと直すたびに往復が増える。</summary>
    [Fact]
    public void EveryMissingDependencyIsReportedAtOnce()
    {
        var error = Assert.Throws<StartupRequirementException>(
            () => StartupRequirements.Validate(Build(), loadedConfigFilePath: null));

        Assert.Contains("mirakurunPath", error.Message, StringComparison.Ordinal);
        Assert.Contains("ConnectionStrings__PostgreSQL", error.Message, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------ 起動してよい場合

    [Fact]
    public void WithEverythingConfiguredTheStartupProceeds()
    {
        IConfiguration configuration = Build(
            ("Mirakurun:BaseUrl", "http://192.168.0.2:40772"),
            ("ConnectionStrings:PostgreSQL", PostgresConnection));

        StartupRequirements.Validate(configuration, loadedConfigFilePath: "/etc/config.yml");
    }

    /// <summary>
    /// 縮退運転は明示したときだけ。既定で有効にすると、設定漏れが起動失敗ではなく
    /// 「一部の機能だけ静かに動かない」形で現れて原因に辿り着けない。
    /// </summary>
    [Fact]
    public void DegradedStartupIsOptInAndOffByDefault()
    {
        Assert.False(StartupRequirements.IsDegradedStartupAllowed(Build()));
        Assert.True(StartupRequirements.IsDegradedStartupAllowed(
            Build(("AllowDegradedStartup", "true"))));

        // 明示すれば、何も設定されていなくても通る (契約試験用)。
        StartupRequirements.Validate(
            Build(("AllowDegradedStartup", "true")),
            loadedConfigFilePath: null);
    }

    [Fact]
    public void TheDependencyInjectionEntryPointRefusesAnIncompleteConfiguration()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        Assert.Throws<StartupRequirementException>(
            () => services.AddTnlaStationInfrastructure(Build()));
    }

    // ------------------------------------------------------------------ 配布している例が正しいか

    /// <summary>
    /// リポジトリが配っている <c>config/config.yml.example</c> を実際に読み込み、
    /// そのまま使えば縮退運転にならないことを確かめる。
    ///
    /// 例そのものが必要な項目を欠いていても同じ縮退運転になるので、中身まで見る。
    /// </summary>
    [Fact]
    public void TheShippedConfigExampleStartsWithoutDegrading()
    {
        string examplePath = FindConfigExample();
        IConfigurationRoot configuration = new ConfigurationBuilder()
            .AddEpgStationConfigFile(examplePath, optional: false, reloadOnChange: false)
            // 接続文字列だけはパスワードを含むため config.yml ではなく環境変数で渡す運用。
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:PostgreSQL"] = PostgresConnection,
            })
            .Build();

        // 例をそのまま使えば起動できる。
        StartupRequirements.Validate(configuration, examplePath);

        // Mirakurun の接続先が実際に入っていること。空のままでは EPG 更新も録画も動かない。
        string? mirakurunPath = configuration["Mirakurun:BaseUrl"];
        Assert.False(string.IsNullOrWhiteSpace(mirakurunPath));

        // 保存先が 1 つ以上あること。無いと録画の書き先が決まらない。
        Assert.False(string.IsNullOrWhiteSpace(configuration["Storage:RecordedDirectories:0:Path"]));

        // ブラウザへ知らせる socket.io のポート。省くと待受ポートがそのまま案内され、
        // gateway 越しの構成ではブラウザから繋がらない。
        Assert.False(string.IsNullOrWhiteSpace(configuration["Server:ClientSocketIoPort"]));
    }

    private static string FindConfigExample()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string candidate = Path.Combine(directory.FullName, "TNLAStation", "config", "config.yml.example");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        string fixture = Path.Combine(AppContext.BaseDirectory, "Fixtures", "config.yml.example");
        if (File.Exists(fixture))
        {
            return fixture;
        }

        throw new FileNotFoundException(
            "TNLAStation/config/config.yml.example とテスト用フィクスチャのどちらも見つかりません。");
    }
}
