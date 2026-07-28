using TNLAStation.Infrastructure.Logging;

namespace TNLAStation.Infrastructure.Tests;

public sealed class EpgStationLogProviderTests
{
    [Fact]
    public void LoadsLog4jsYamlAndWritesExpandedRotatingFile()
    {
        string root = Path.Combine(Path.GetTempPath(), $"tnla-log-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            string config = Path.Combine(root, "operatorLogConfig.yml");
            File.WriteAllText(config, """
                appenders:
                  system:
                    type: file
                    maxLogSize: 10
                    backups: 2
                    filename: "%OperatorSystem%"
                categories:
                  default:
                    appenders: [system]
                    level: info
                  system:
                    appenders: [system]
                    level: info
                """);

            EpgLogProfile profile = Assert.IsType<EpgLogProfile>(EpgLogProfile.Load(config, root));
            profile.Write("operator", "system", "first message");
            profile.Write("operator", "system", "second message");

            string log = Path.Combine(root, "Operator", "system.log");
            Assert.True(File.Exists(log));
            Assert.True(File.Exists($"{log}.1"));
            Assert.Contains("second message", File.ReadAllText(log));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
