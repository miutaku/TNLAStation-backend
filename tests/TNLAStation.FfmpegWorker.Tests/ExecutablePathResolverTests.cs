using TNLAStation.FfmpegWorker.Options;

namespace TNLAStation.FfmpegWorker.Tests;

public sealed class ExecutablePathResolverTests
{
    [Fact]
    public void ExistingConfiguredPathIsPreserved()
    {
        string path = Environment.ProcessPath!;

        Assert.Equal(path, ExecutablePathResolver.Resolve(path));
    }

    [Fact]
    public void MissingAbsolutePathFallsBackToTheSameFileNameOnPath()
    {
        string executable = Path.GetFileName(Environment.ProcessPath!);
        string configured = Path.Combine(Path.DirectorySeparatorChar.ToString(), "missing", executable);
        string resolved = ExecutablePathResolver.Resolve(configured);

        Assert.Equal(executable, Path.GetFileName(resolved));
        Assert.True(File.Exists(resolved));
    }

    [Fact]
    public void UnresolvablePathIsPreservedForAUsefulStartupError()
    {
        const string configured = "/missing/tnlastation-not-a-real-executable";

        Assert.Equal(configured, ExecutablePathResolver.Resolve(configured));
    }
}
