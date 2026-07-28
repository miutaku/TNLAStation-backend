using System.Diagnostics;
using System.Globalization;
using TNLAStation.FfmpegWorker.Processes;

namespace TNLAStation.FfmpegWorker.Tests;

public sealed class ChildProcessLifetimeTests
{
    [Fact]
    public async Task StopAsyncKillsAndReapsTheWholeProcessTree()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"tnla-process-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string childPidPath = Path.Combine(directory, "child.pid");

        var startInfo = new ProcessStartInfo("/bin/sh")
        {
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add(
            """sleep 300 & child=$!; printf '%s' "$child" > "$1"; wait "$child" """);
        startInfo.ArgumentList.Add("tnla-process-test");
        startInfo.ArgumentList.Add(childPidPath);

        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start the process fixture.");

        try
        {
            await WaitUntilAsync(() => File.Exists(childPidPath), TimeSpan.FromSeconds(5));
            int childPid = int.Parse(
                await File.ReadAllTextAsync(childPidPath),
                NumberStyles.None,
                CultureInfo.InvariantCulture);

            Assert.True(await ChildProcessLifetime.StopAsync(process, TimeSpan.FromSeconds(5)));
            Assert.True(process.HasExited);

            await WaitUntilAsync(() => !IsAlive(childPid), TimeSpan.FromSeconds(5));
            Assert.False(IsAlive(childPid));
        }
        finally
        {
            await ChildProcessLifetime.StopAsync(process, TimeSpan.FromSeconds(1));
            Directory.Delete(directory, recursive: true);
        }
    }

    private static bool IsAlive(int processId)
    {
        try
        {
            using Process process = Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        long startedAt = Stopwatch.GetTimestamp();
        while (!condition())
        {
            if (Stopwatch.GetElapsedTime(startedAt) >= timeout)
            {
                throw new TimeoutException("The process fixture did not reach the expected state.");
            }

            await Task.Delay(20);
        }
    }
}
