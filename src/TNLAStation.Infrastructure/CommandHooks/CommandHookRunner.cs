using System.Diagnostics;
using System.Globalization;
using Microsoft.Extensions.Logging;
using TNLAStation.Application.Abstractions;

namespace TNLAStation.Infrastructure.CommandHooks;

/// <summary>
/// 設定された外部コマンドを実行する。
///
/// EPGStation は <c>EPGStation/src/model/operator/externalCommand/ExternalCommandManageModel.ts</c>。
/// 環境変数の作り方が外から見えるので、次の 3 点を写している。
///
/// 1. 親の環境は受け継がない。EPGStation の <c>spawn(..., { env: { PATH: process.env['PATH'], ... } })</c> は
///    <c>env</c> を丸ごと置き換えるので、子プロセスから見えるのは <c>PATH</c> と各フックの変数だけ。
///    継承させると、スクリプトが偶然同名の変数を拾って挙動が変わる。
/// 2. <c>null</c> は文字列 <c>"null"</c> になる。Node は <c>`${key}=${value}`</c> で組み立てるため。
///    値が <c>undefined</c> のものだけが変数ごと消える。
/// 3. どこが <c>null</c> でどこが空文字かはフックごとに違う。エンコード完了だけは
///    <c>VIDEOFILEID</c>・<c>DESCRIPTION</c> 系を空文字にしており、それも EPGStation のまま。
/// </summary>
public sealed partial class CommandHookRunner(ILogger<CommandHookRunner> logger) : ICommandHookRunner
{
    /// <summary>Node が <c>null</c> を文字列化した結果。</summary>
    private const string JsNull = "null";

    public void RunReserveHook(string? command, ReserveHookPayload payload)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return;
        }

        ArgumentNullException.ThrowIfNull(payload);

        Run(command, new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["RESERVEID"] = Number(payload.ReserveId),
            ["PROGRAMID"] = Number(payload.ProgramId),
            ["CHANNELTYPE"] = payload.ChannelType ?? JsNull,
            ["CHANNELID"] = Number(payload.ChannelId),
            ["CHANNELNAME"] = payload.ChannelName ?? JsNull,
            ["HALF_WIDTH_CHANNELNAME"] = payload.HalfWidthChannelName ?? JsNull,
            ["STARTAT"] = Number(payload.StartAt),
            ["ENDAT"] = Number(payload.EndAt),
            ["DURATION"] = Number(payload.EndAt - payload.StartAt),
            ["NAME"] = payload.Name ?? JsNull,
            ["HALF_WIDTH_NAME"] = payload.HalfWidthName ?? JsNull,
            ["DESCRIPTION"] = payload.Description ?? JsNull,
            ["HALF_WIDTH_DESCRIPTION"] = payload.HalfWidthDescription ?? JsNull,
            ["EXTENDED"] = payload.Extended ?? JsNull,
            ["HALF_WIDTH_EXTENDED"] = payload.HalfWidthExtended ?? JsNull,
        });
    }

    public void RunRecordedHook(string? command, RecordedHookPayload payload)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return;
        }

        ArgumentNullException.ThrowIfNull(payload);

        Run(command, new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["RECORDEDID"] = Number(payload.RecordedId),
            ["PROGRAMID"] = Number(payload.ProgramId),
            ["CHANNELTYPE"] = payload.ChannelType ?? JsNull,
            ["CHANNELID"] = Number(payload.ChannelId),
            ["CHANNELNAME"] = payload.ChannelName ?? JsNull,
            ["HALF_WIDTH_CHANNELNAME"] = payload.HalfWidthChannelName ?? JsNull,
            ["STARTAT"] = Number(payload.StartAt),
            ["ENDAT"] = Number(payload.EndAt),
            ["DURATION"] = Number(payload.EndAt - payload.StartAt),
            ["NAME"] = payload.Name ?? JsNull,
            ["HALF_WIDTH_NAME"] = payload.HalfWidthName ?? JsNull,
            ["DESCRIPTION"] = payload.Description ?? JsNull,
            ["HALF_WIDTH_DESCRIPTION"] = payload.HalfWidthDescription ?? JsNull,
            ["EXTENDED"] = payload.Extended ?? JsNull,
            ["HALF_WIDTH_EXTENDED"] = payload.HalfWidthExtended ?? JsNull,
            ["RECPATH"] = payload.RecPath ?? JsNull,
            ["LOGPATH"] = payload.LogPath ?? JsNull,
            ["ERROR_CNT"] = Number(payload.ErrorCount),
            ["DROP_CNT"] = Number(payload.DropCount),
            ["SCRAMBLING_CNT"] = Number(payload.ScramblingCount),
        });
    }

    public void RunEncodeFinishHook(string? command, EncodeFinishHookPayload payload)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return;
        }

        ArgumentNullException.ThrowIfNull(payload);

        // EPGStation の createFinishEncodeCmd は、ここだけ null ではなく空文字を入れる箇所がある。
        Run(command, new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["RECORDEDID"] = Number(payload.RecordedId),
            ["VIDEOFILEID"] = payload.VideoFileId is { } videoFileId
                ? videoFileId.ToString(CultureInfo.InvariantCulture)
                : string.Empty,
            ["OUTPUTPATH"] = payload.OutputPath ?? JsNull,
            ["MODE"] = payload.Mode ?? JsNull,
            ["NAME"] = payload.Name ?? JsNull,
            ["HALF_WIDTH_NAME"] = payload.HalfWidthName ?? JsNull,
            ["DESCRIPTION"] = payload.Description ?? string.Empty,
            ["HALF_WIDTH_DESCRIPTION"] = payload.HalfWidthDescription ?? string.Empty,
            ["EXTENDED"] = payload.Extended ?? string.Empty,
            ["HALF_WIDTH_EXTENDED"] = payload.HalfWidthExtended ?? string.Empty,
            ["CHANNELID"] = Number(payload.ChannelId),
            ["CHANNELNAME"] = payload.ChannelName ?? string.Empty,
            ["HALF_WIDTH_CHANNELNAME"] = payload.HalfWidthChannelName ?? string.Empty,
        });
    }

    private static string Number(long value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Number(long? value) =>
        value is { } number ? number.ToString(CultureInfo.InvariantCulture) : JsNull;

    private void Run(string command, IReadOnlyDictionary<string, string> variables)
    {
        try
        {
            string[] parts = ShellCommandLine.Split(command);
            if (parts.Length == 0)
            {
                return;
            }

            var startInfo = new ProcessStartInfo(parts[0]) { UseShellExecute = false };
            foreach (string argument in parts.Skip(1))
            {
                startInfo.ArgumentList.Add(argument);
            }

            // 親の環境をそのまま渡さない。EPGStation が渡すのは PATH とフックの変数だけ。
            startInfo.Environment.Clear();
            if (Environment.GetEnvironmentVariable("PATH") is { } path)
            {
                startInfo.Environment["PATH"] = path;
            }

            foreach (KeyValuePair<string, string> variable in variables)
            {
                startInfo.Environment[variable.Key] = variable.Value;
            }

            // 完了を待たない。予約や録画そのものの処理を、遅い/固まるスクリプトで止めない。
            Process.Start(startInfo)?.Dispose();
        }
        catch (Exception exception)
        {
            LogHookFailed(logger, command, exception);
        }
    }

    [LoggerMessage(
        EventId = 8000,
        Level = LogLevel.Warning,
        Message = "Could not run the command hook: {Command}")]
    private static partial void LogHookFailed(ILogger logger, string command, Exception exception);
}
