using System.Diagnostics;
using System.Globalization;
using Microsoft.Extensions.Options;
using TNLAStation.FfmpegWorker.Media;
using TNLAStation.FfmpegWorker.Options;
using TNLAStation.FfmpegWorker.Processes;

namespace TNLAStation.FfmpegWorker.Tests;

/// <summary>
/// DB や待ち行列は backend (EncodeWorker/RemoteEncodeExecutor) 側の関心事なので、ここでは
/// 実際に動く親子プロセスまで含めて、取り消しがプロセスツリーを確実に畳むことだけを確かめる。
/// </summary>
public sealed class EncodeRunnerCancellationTests
{
    [Fact]
    public async Task CancelStopsTheWholeProcessTree()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        string directory = Path.Combine(Path.GetTempPath(), $"tnla-encode-runner-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string sourcePath = Path.Combine(directory, "input.ts");
        string outputPath = Path.Combine(directory, "output.mp4");
        string pidPath = Path.Combine(directory, "ffmpeg.pids");
        string executable = Path.Combine(directory, "fake-ffmpeg");
        await File.WriteAllBytesAsync(sourcePath, [0x47, 0x00, 0x00, 0x10]);
        await File.WriteAllTextAsync(
            executable,
            $"""
            #!/bin/sh
            for output do :; done
            sleep 300 &
            child=$!
            printf '%s\n%s\n' "$$" "$child" > "{pidPath}"
            : > "$output"
            printf 'out_time_ms=1000000\n' >&2
            wait "$child"
            """);
        File.SetUnixFileMode(
            executable,
            UnixFileMode.UserRead |
            UnixFileMode.UserWrite |
            UnixFileMode.UserExecute);

        var gate = new ProcessGate(Microsoft.Extensions.Options.Options.Create(new FfmpegOptions()));
        var runner = new EncodeRunner(
            new MediaProbeRunner(Microsoft.Extensions.Options.Options.Create(new FfmpegOptions { FfprobePath = "true" }), gate),
            Microsoft.Extensions.Options.Options.Create(new FfmpegOptions { FfmpegPath = executable }),
            gate);

        using var cancellation = new CancellationTokenSource();

        try
        {
            Task<bool> run = runner.RunAsync(
                sourcePath,
                outputPath,
                arguments: ["-c:v", "libx264"],
                command: null,
                rateTimeoutMultiplier: null,
                environmentVariables: new Dictionary<string, string>(),
                onProgress: (_, _, _) => Task.CompletedTask,
                cancellation.Token);

            await WaitUntilAsync(() => File.Exists(pidPath), TimeSpan.FromSeconds(10));
            int[] processIds = (await File.ReadAllLinesAsync(pidPath))
                .Select(line => int.Parse(line, NumberStyles.None, CultureInfo.InvariantCulture))
                .ToArray();
            Assert.Equal(2, processIds.Length);

            await cancellation.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);

            await WaitUntilAsync(
                () => processIds.All(processId => !IsAlive(processId)),
                TimeSpan.FromSeconds(5));
            Assert.All(processIds, processId => Assert.False(IsAlive(processId)));
        }
        finally
        {
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
                throw new TimeoutException("The encode runner did not reach the expected state.");
            }

            await Task.Delay(20);
        }
    }
}
