using System.Diagnostics;
using System.Globalization;
using System.Runtime.ExceptionServices;
using Microsoft.Extensions.Options;
using TNLAStation.FfmpegWorker.Options;
using TNLAStation.FfmpegWorker.Processes;

namespace TNLAStation.FfmpegWorker.Media;

/// <summary>
/// 録画済みファイル 1 本のエンコード。呼び出し側 (backend の EncodeWorker) は待ち行列と
/// DB 更新だけを持ち、実際に ffmpeg (または任意コマンド) を回してここへ進捗を尋ねる。
/// </summary>
public sealed class EncodeRunner(MediaProbeRunner probe, IOptions<FfmpegOptions> options, ProcessGate gate)
{
    private readonly FfmpegOptions options = options.Value;

    /// <param name="command">
    /// 設定されていれば、固定の ffmpeg 引数の代わりにこのコマンドをそのまま実行する
    /// (EPGStation の encode.cmd 相当)。%INPUT%/%OUTPUT%/%FFMPEG%/%FFPROBE% を置換する。
    /// 進捗 (パーセント) は ffmpeg の -progress 前提のため、このモードでは追わない。
    /// </param>
    /// <param name="rateTimeoutMultiplier">
    /// 録画時間 (秒) にこの値を掛けた時間を超えたら打ち切る (EPGStation の encode.rate 相当)。
    /// 動画の長さが分からない、または未設定なら打ち切らない。
    /// </param>
    public async Task<bool> RunAsync(
        string input,
        string output,
        IReadOnlyList<string> arguments,
        string? command,
        double? rateTimeoutMultiplier,
        IReadOnlyDictionary<string, string> environmentVariables,
        Func<int?, string?, CancellationToken, Task> onProgress,
        CancellationToken cancellationToken)
    {
        double? totalSeconds = await probe.GetDurationSecondsAsync(input, cancellationToken);

        await using IAsyncDisposable lease = await gate.AcquireAsync(cancellationToken);

        using var timeoutSource = new CancellationTokenSource();
        if (totalSeconds is { } durationSeconds and > 0 && rateTimeoutMultiplier is { } rate and > 0)
        {
            timeoutSource.CancelAfter(TimeSpan.FromSeconds(durationSeconds * rate));
        }

        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutSource.Token);
        CancellationToken effectiveToken = linked.Token;

        var startInfo = new ProcessStartInfo
        {
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        if (string.IsNullOrWhiteSpace(command))
        {
            startInfo.FileName = options.FfmpegPath;
            foreach (string argument in CreateArguments(input, output, arguments))
            {
                startInfo.ArgumentList.Add(argument);
            }
        }
        else
        {
            string[] parts = ShellCommandLine.Split(Substitute(command, input, output));
            if (parts.Length == 0)
            {
                return false;
            }

            startInfo.FileName = parts[0];
            foreach (string argument in parts.Skip(1))
            {
                startInfo.ArgumentList.Add(argument);
            }
        }

        // EPGStation の encode.cmd と同じく、置換トークンとは別に環境変数としても渡す。
        foreach ((string key, string value) in environmentVariables)
        {
            startInfo.Environment[key] = value;
        }

        startInfo.Environment["INPUT"] = input;
        startInfo.Environment["OUTPUT"] = output;
        startInfo.Environment["FFMPEG"] = options.FfmpegPath;
        startInfo.Environment["FFPROBE"] = options.FfprobePath;

        using Process? process = Process.Start(startInfo);
        if (process is null)
        {
            return false;
        }

        // ffmpeg は進み具合 (-progress の key=value) と、通常のログを同じ標準エラーへ混ぜて出す。
        // key=value は進捗として扱い、それ以外の行を画面に見せるログとしてためる。ログは
        // 新しい行だけを一定量残し、進捗更新に合わせて間引いて呼び出し側へ渡す。
        // 任意コマンドのときは -progress を前提にできないので、行はすべてログとして扱う。
        bool isFfmpeg = string.IsNullOrWhiteSpace(command);
        var log = new EncodeLogBuffer();
        int? lastPercent = null;
        long lastFlushStamp = Stopwatch.GetTimestamp();
        bool dirty = false;
        bool stopped;
        bool succeeded = false;
        Exception? failure = null;

        async Task FlushAsync()
        {
            await onProgress(lastPercent, log.Snapshot(), cancellationToken);
            dirty = false;
            lastFlushStamp = Stopwatch.GetTimestamp();
        }

        try
        {
            while (await process.StandardError.ReadLineAsync(effectiveToken) is { } line)
            {
                if (isFfmpeg && IsProgressLine(line))
                {
                    if (totalSeconds is { } total and > 0 && TryReadOutTime(line, out double seconds))
                    {
                        int percent = (int)Math.Clamp(seconds / total * 100, 0, 100);
                        if (percent != lastPercent)
                        {
                            lastPercent = percent;
                            dirty = true;
                        }
                    }
                }
                else
                {
                    log.Append(line);
                    dirty = true;
                }

                // 書き込みが多くなりすぎないよう、変化があっても 1.5 秒に 1 度だけ反映する。
                if (dirty && Stopwatch.GetElapsedTime(lastFlushStamp) >= TimeSpan.FromMilliseconds(1500))
                {
                    await FlushAsync();
                }
            }

            if (dirty)
            {
                await FlushAsync();
            }

            await process.WaitForExitAsync(effectiveToken);
            succeeded = process.ExitCode == 0;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // 呼び出し元による本当の取り消し。そのまま伝える。
            throw;
        }
        catch (OperationCanceledException)
        {
            // rate タイムアウト。取り消しではなく、暴走したエンコードの失敗として扱う。
            succeeded = false;
        }
        catch (Exception exception)
        {
            failure = exception;
        }
        finally
        {
            stopped = await ChildProcessLifetime.StopAsync(process);
        }

        if (!stopped)
        {
            throw new InvalidOperationException("EncodeProcessDidNotExit");
        }

        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }

        return succeeded;
    }

    /// <summary>
    /// -progress が吐く 1 行かどうか。key=value 形式で、キーは英小文字・数字・アンダースコアだけ。
    /// ffmpeg 本体のログ (Input #0 …、[libx264 @ …] …) は空白や大文字・記号で始まるので当たらない。
    /// </summary>
    private static bool IsProgressLine(string line)
    {
        int equals = line.IndexOf('=', StringComparison.Ordinal);
        if (equals <= 0)
        {
            return false;
        }

        for (int index = 0; index < equals; index++)
        {
            char c = line[index];
            if (!(char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c == '_'))
            {
                return false;
            }
        }

        return true;
    }

    private static string[] CreateArguments(string input, string output, IReadOnlyList<string> modeArguments)
    {
        var arguments = new List<string>
        {
            "-hide_banner",
            "-nostats",
            // 進み具合を読むために、経過を標準エラーへ機械が読める形で出させる。
            "-progress", "pipe:2",
            // ログ表示のため、入出力やストリームの対応・警告まで出す。周期的な統計は -nostats で止める。
            "-loglevel", "info",
            "-fflags", "+discardcorrupt",
            "-analyzeduration", "10M",
            "-probesize", "32M",
            "-dual_mono_mode", "main",
            "-i", input,
            "-map", "0:v:0",
            "-map", "0:a:0",
            "-ignore_unknown",
            "-sn",
            "-dn",
        };
        arguments.AddRange(modeArguments);
        arguments.Add("-y");
        arguments.Add(output);
        return [.. arguments];
    }

    private string Substitute(string command, string input, string output) => command
        .Replace("%INPUT%", input, StringComparison.Ordinal)
        .Replace("%OUTPUT%", output, StringComparison.Ordinal)
        .Replace("%FFMPEG%", options.FfmpegPath, StringComparison.Ordinal)
        .Replace("%FFPROBE%", options.FfprobePath, StringComparison.Ordinal);

    private static bool TryReadOutTime(string line, out double seconds)
    {
        seconds = 0;
        const string key = "out_time_ms=";
        if (!line.StartsWith(key, StringComparison.Ordinal))
        {
            return false;
        }

        // 名前に反して単位はマイクロ秒。ffmpeg の出力がそうなっている。
        if (!long.TryParse(line[key.Length..], NumberStyles.Integer, CultureInfo.InvariantCulture, out long value))
        {
            return false;
        }

        seconds = value / 1_000_000d;
        return true;
    }
}

/// <summary>
/// ffmpeg ログの末尾だけをためる。行数と文字数の両方に上限を設け、古い行から捨てる。
/// ネットワークに乗せるので、際限なく伸ばさない。
/// </summary>
internal sealed class EncodeLogBuffer
{
    private const int MaxLines = 400;
    private const int MaxChars = 32_000;

    private readonly Queue<string> lines = new();
    private int chars;

    public void Append(string line)
    {
        lines.Enqueue(line);
        chars += line.Length + 1;
        while (lines.Count > MaxLines || chars > MaxChars)
        {
            string removed = lines.Dequeue();
            chars -= removed.Length + 1;
        }
    }

    public string? Snapshot() => lines.Count == 0 ? null : string.Join('\n', lines);
}
