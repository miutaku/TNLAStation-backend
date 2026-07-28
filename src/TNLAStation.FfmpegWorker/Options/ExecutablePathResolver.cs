namespace TNLAStation.FfmpegWorker.Options;

internal static class ExecutablePathResolver
{
    public static string Resolve(string configuredPath)
    {
        if (string.IsNullOrWhiteSpace(configuredPath) || File.Exists(configuredPath))
        {
            return configuredPath;
        }

        string fileName = Path.GetFileName(configuredPath);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return configuredPath;
        }

        foreach (string directory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
                     .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            string candidate = Path.Combine(directory, fileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return configuredPath;
    }
}
