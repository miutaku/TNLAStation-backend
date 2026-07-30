using TNLAStation.Application.Abstractions;

namespace TNLAStation.FfmpegWorker.Media;

/// <summary>共有DBから取得したjobを、このPod内のffmpegで実行する。</summary>
public sealed class LocalEncodeExecutor(EncodeRunner runner) : IEncodeExecutor
{
    public Task<bool> RunAsync(
        string inputPath,
        string outputPath,
        IReadOnlyList<string> arguments,
        string? command,
        double? rateTimeoutMultiplier,
        IReadOnlyDictionary<string, string> environmentVariables,
        Func<int?, string?, CancellationToken, Task> onProgress,
        CancellationToken cancellationToken) =>
        runner.RunAsync(
            inputPath,
            outputPath,
            arguments,
            command,
            rateTimeoutMultiplier,
            environmentVariables,
            onProgress,
            cancellationToken);
}
