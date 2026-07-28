using Microsoft.Extensions.Logging;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace TNLAStation.Infrastructure.Logging;

public static class EpgStationLogExtensions
{
    public static ILoggingBuilder AddEpgStationLogConfigs(
        this ILoggingBuilder logging,
        string configDirectory,
        bool worker = false)
    {
        ArgumentNullException.ThrowIfNull(logging);
        string logRoot = Environment.GetEnvironmentVariable("EPGSTATION_LOG_DIR")
            ?? Path.Combine(AppContext.BaseDirectory, "logs");
        EpgLogProfile? service = EpgLogProfile.Load(Path.Combine(configDirectory, "serviceLogConfig.yml"), logRoot);
        EpgLogProfile? profile = worker
            ? service
            : EpgLogProfile.Combine(
                EpgLogProfile.Load(Path.Combine(configDirectory, "operatorLogConfig.yml"), logRoot),
                service,
                EpgLogProfile.Load(Path.Combine(configDirectory, "epgUpdaterLogConfig.yml"), logRoot));
        if (profile is not null)
        {
            logging.AddProvider(new EpgStationLogProvider(profile, worker));
        }

        return logging;
    }
}

internal sealed class EpgStationLogProvider(EpgLogProfile profile, bool worker) : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new EpgStationLogger(profile, categoryName, worker);
    public void Dispose() { }
}

internal sealed class EpgStationLogger(EpgLogProfile profile, string sourceCategory, bool worker) : ILogger
{
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) =>
        logLevel != LogLevel.None && logLevel >= profile.MinimumLevel(RouteProfile(), RouteCategory());

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        string profileName = RouteProfile();
        string category = RouteCategory();
        if (!IsEnabled(logLevel))
        {
            return;
        }

        string message = $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff} [{logLevel.ToString().ToUpperInvariant()}] {formatter(state, exception)}";
        if (exception is not null)
        {
            message += Environment.NewLine + exception;
        }

        profile.Write(profileName, category, message);
    }

    private string RouteProfile()
    {
        if (worker)
        {
            return "service";
        }

        if (sourceCategory.Contains("EpgSync", StringComparison.OrdinalIgnoreCase))
        {
            return "epgUpdater";
        }

        return sourceCategory.Contains("Streaming", StringComparison.OrdinalIgnoreCase) ||
               sourceCategory.Contains("Recording", StringComparison.OrdinalIgnoreCase) ||
               sourceCategory.Contains("Encode", StringComparison.OrdinalIgnoreCase)
            ? "service"
            : "operator";
    }

    private string RouteCategory()
    {
        if (sourceCategory.Contains("Encode", StringComparison.OrdinalIgnoreCase))
        {
            return "encode";
        }

        if (sourceCategory.Contains("Stream", StringComparison.OrdinalIgnoreCase) ||
            sourceCategory.Contains("Recording", StringComparison.OrdinalIgnoreCase))
        {
            return "stream";
        }

        if (sourceCategory.Contains("Middleware", StringComparison.OrdinalIgnoreCase) ||
            sourceCategory.Contains("Endpoint", StringComparison.OrdinalIgnoreCase))
        {
            return "access";
        }

        return "system";
    }
}

internal sealed class EpgLogProfile
{
    private static readonly object FileGate = new();
    private readonly Dictionary<string, EpgLogConfig> configs = new(StringComparer.OrdinalIgnoreCase);

    public static EpgLogProfile? Load(string path, string logRoot)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();
        EpgLogYaml yaml = deserializer.Deserialize<EpgLogYaml>(File.ReadAllText(path));
        string profileName = Path.GetFileName(path) switch
        {
            "serviceLogConfig.yml" => "service",
            "epgUpdaterLogConfig.yml" => "epgUpdater",
            _ => "operator",
        };
        var result = new EpgLogProfile();
        result.configs[profileName] = EpgLogConfig.Create(yaml, logRoot);
        return result;
    }

    public static EpgLogProfile? Combine(params EpgLogProfile?[] profiles)
    {
        var result = new EpgLogProfile();
        foreach (EpgLogProfile? profile in profiles)
        {
            if (profile is not null)
            {
                foreach ((string name, EpgLogConfig config) in profile.configs)
                {
                    result.configs[name] = config;
                }
            }
        }

        return result.configs.Count == 0 ? null : result;
    }

    public LogLevel MinimumLevel(string profile, string category) =>
        Find(profile)?.MinimumLevel(category) ?? LogLevel.None;

    public void Write(string profile, string category, string message)
    {
        EpgLogConfig? config = Find(profile);
        if (config is null)
        {
            return;
        }

        foreach (EpgFileAppender appender in config.FileAppenders(category))
        {
            lock (FileGate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(appender.Filename)!);
                Rotate(appender);
                File.AppendAllText(appender.Filename, message + Environment.NewLine);
            }
        }
    }

    private EpgLogConfig? Find(string profile) =>
        configs.TryGetValue(profile, out EpgLogConfig? config) ? config : null;

    private static void Rotate(EpgFileAppender appender)
    {
        if (appender.MaxLogSize <= 0 || !File.Exists(appender.Filename) ||
            new FileInfo(appender.Filename).Length < appender.MaxLogSize)
        {
            return;
        }

        for (int index = Math.Max(1, appender.Backups); index >= 1; index--)
        {
            string source = index == 1 ? appender.Filename : $"{appender.Filename}.{index - 1}";
            string target = $"{appender.Filename}.{index}";
            if (File.Exists(source))
            {
                File.Move(source, target, overwrite: true);
            }
        }
    }
}

internal sealed record EpgLogConfig(
    Dictionary<string, EpgFileAppender> Appenders,
    Dictionary<string, EpgLogCategory> Categories)
{
    public static EpgLogConfig Create(EpgLogYaml yaml, string logRoot)
    {
        var appenders = new Dictionary<string, EpgFileAppender>(StringComparer.OrdinalIgnoreCase);
        foreach ((string name, EpgAppenderYaml value) in yaml.Appenders)
        {
            if (!string.Equals(value.Type, "file", StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrWhiteSpace(value.Filename))
            {
                continue;
            }

            appenders[name] = new EpgFileAppender(
                ExpandFilename(value.Filename, logRoot),
                value.MaxLogSize,
                value.Backups);
        }

        return new EpgLogConfig(appenders, new Dictionary<string, EpgLogCategory>(
            yaml.Categories.ToDictionary(
                pair => pair.Key,
                pair => new EpgLogCategory(pair.Value.Appenders, ParseLevel(pair.Value.Level))),
            StringComparer.OrdinalIgnoreCase));
    }

    public LogLevel MinimumLevel(string category) =>
        Categories.TryGetValue(category, out EpgLogCategory? value)
            ? value.Level
            : Categories.TryGetValue("default", out value) ? value.Level : LogLevel.Information;

    public IEnumerable<EpgFileAppender> FileAppenders(string category)
    {
        EpgLogCategory? value = Categories.TryGetValue(category, out EpgLogCategory? exact)
            ? exact
            : Categories.GetValueOrDefault("default");
        return value?.Appenders
            .Where(Appenders.ContainsKey)
            .Select(name => Appenders[name]) ?? [];
    }

    private static string ExpandFilename(string filename, string root)
    {
        Dictionary<string, string> values = new()
        {
            ["Operator"] = "Operator",
            ["Service"] = "Service",
            ["EPGUpdater"] = "EPGUpdater",
        };
        foreach ((string token, string directory) in values)
        {
            foreach (string category in new[] { "System", "Access", "Stream", "Encode" })
            {
                filename = filename.Replace(
                    $"%{token}{category}%",
                    Path.Combine(root, directory, $"{category.ToLowerInvariant()}.log"),
                    StringComparison.Ordinal);
            }
        }

        return filename;
    }

    private static LogLevel ParseLevel(string value) => value.ToLowerInvariant() switch
    {
        "trace" => LogLevel.Trace,
        "debug" => LogLevel.Debug,
        "warn" => LogLevel.Warning,
        "error" => LogLevel.Error,
        "fatal" => LogLevel.Critical,
        "off" => LogLevel.None,
        _ => LogLevel.Information,
    };
}

internal sealed record EpgFileAppender(string Filename, long MaxLogSize, int Backups);
internal sealed record EpgLogCategory(IReadOnlyList<string> Appenders, LogLevel Level);
internal sealed class EpgLogYaml
{
    public Dictionary<string, EpgAppenderYaml> Appenders { get; init; } = [];
    public Dictionary<string, EpgCategoryYaml> Categories { get; init; } = [];
}
internal sealed class EpgAppenderYaml
{
    public string Type { get; init; } = "";
    public long MaxLogSize { get; init; }
    public int Backups { get; init; }
    public string Filename { get; init; } = "";
    public string? Pattern { get; init; }
}
internal sealed class EpgCategoryYaml
{
    public List<string> Appenders { get; init; } = [];
    public string Level { get; init; } = "info";
}
