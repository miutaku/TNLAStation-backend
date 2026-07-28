using System.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using TNLAStation.Application.Abstractions;
using TNLAStation.Infrastructure.CommandHooks;

namespace TNLAStation.Infrastructure.Tests;

public sealed class CommandHookRunnerTests
{
    [Fact]
    public async Task RunReserveHookPassesTheDocumentedEnvironmentVariables()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        string directory = Path.Combine(Path.GetTempPath(), $"tnla-hook-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string outputPath = Path.Combine(directory, "out.txt");
        string script = CreateDumpScript(directory, outputPath, "RESERVEID", "CHANNELTYPE", "CHANNELID", "CHANNELNAME", "NAME", "DURATION");

        try
        {
            var runner = new CommandHookRunner(NullLogger<CommandHookRunner>.Instance);
            runner.RunReserveHook(script, new ReserveHookPayload(
                ReserveId: 7,
                ProgramId: 123,
                ChannelId: 1,
                ChannelName: "テスト放送",
                HalfWidthChannelName: "ﾃｽﾄﾎｳｿｳ",
                StartAt: 1_000,
                EndAt: 61_000,
                Name: "番組名",
                HalfWidthName: "ﾊﾞﾝｸﾞﾐﾒｲ",
                ChannelType: "GR"));

            IReadOnlyDictionary<string, string> variables = await ReadDumpAsync(outputPath);
            Assert.Equal("7", variables["RESERVEID"]);
            Assert.Equal("GR", variables["CHANNELTYPE"]);
            Assert.Equal("1", variables["CHANNELID"]);
            Assert.Equal("テスト放送", variables["CHANNELNAME"]);
            Assert.Equal("番組名", variables["NAME"]);
            Assert.Equal("60000", variables["DURATION"]);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task RunRecordedHookPassesDropCountsWhenPresent()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        string directory = Path.Combine(Path.GetTempPath(), $"tnla-hook-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string outputPath = Path.Combine(directory, "out.txt");
        string script = CreateDumpScript(directory, outputPath, "RECORDEDID", "RECPATH", "ERROR_CNT", "DROP_CNT", "SCRAMBLING_CNT");

        try
        {
            var runner = new CommandHookRunner(NullLogger<CommandHookRunner>.Instance);
            runner.RunRecordedHook(script, new RecordedHookPayload(
                RecordedId: 99,
                ProgramId: null,
                ChannelId: 1,
                ChannelName: "CH",
                HalfWidthChannelName: "CH",
                StartAt: 0,
                EndAt: 0,
                Name: "録画",
                HalfWidthName: "録画",
                RecPath: "/recorded/foo.ts",
                ErrorCount: 3,
                DropCount: 2,
                ScramblingCount: 1));

            IReadOnlyDictionary<string, string> variables = await ReadDumpAsync(outputPath);
            Assert.Equal("99", variables["RECORDEDID"]);
            Assert.Equal("/recorded/foo.ts", variables["RECPATH"]);
            Assert.Equal("3", variables["ERROR_CNT"]);
            Assert.Equal("2", variables["DROP_CNT"]);
            Assert.Equal("1", variables["SCRAMBLING_CNT"]);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// 上流の <c>spawn</c> は <c>env</c> を丸ごと差し替える (<c>{ PATH, RESERVEID, ... }</c>)。
    /// 親の環境が漏れるとスクリプトの挙動が変わるので、PATH 以外は渡さないことを固定する。
    /// 根拠: EPGStation/src/model/operator/externalCommand/ExternalCommandManageModel.ts。
    /// </summary>
    [Fact]
    public async Task TheHookProcessOnlyInheritsPathFromTheParentEnvironment()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        string directory = Path.Combine(Path.GetTempPath(), $"tnla-hook-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string outputPath = Path.Combine(directory, "out.txt");

        // env をそのまま吐かせて、鍵の集合ごと確かめる。
        string script = Path.Combine(directory, "dump-all-env");
        File.WriteAllText(script, $"#!/bin/sh\nenv >> \"{outputPath}\"\n");
        File.SetUnixFileMode(script, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        string marker = $"TNLA_HOOK_MARKER_{Guid.NewGuid():N}";
        Environment.SetEnvironmentVariable(marker, "leaked");
        try
        {
            var runner = new CommandHookRunner(NullLogger<CommandHookRunner>.Instance);
            runner.RunReserveHook(script, new ReserveHookPayload(
                ReserveId: 7,
                ProgramId: 123,
                ChannelId: 1,
                ChannelName: "CH",
                HalfWidthChannelName: "CH",
                StartAt: 0,
                EndAt: 0,
                Name: "N",
                HalfWidthName: "N",
                ChannelType: "GR"));

            IReadOnlyDictionary<string, string> variables = await ReadDumpAsync(outputPath);

            Assert.False(variables.ContainsKey(marker));
            Assert.True(variables.ContainsKey("PATH"));

            // /bin/sh 自身が起動時に足す変数は、上流でも同じように現れるので数に入れない。
            string[] shellInjected = ["PWD", "SHLVL", "_", "OLDPWD"];
            Assert.Equal(
                [
                    "CHANNELID", "CHANNELNAME", "CHANNELTYPE", "DESCRIPTION", "DURATION", "ENDAT", "EXTENDED",
                    "HALF_WIDTH_CHANNELNAME", "HALF_WIDTH_DESCRIPTION", "HALF_WIDTH_EXTENDED", "HALF_WIDTH_NAME",
                    "NAME", "PATH", "PROGRAMID", "RESERVEID", "STARTAT",
                ],
                variables.Keys
                    .Where(key => !shellInjected.Contains(key, StringComparer.Ordinal))
                    .Order(StringComparer.Ordinal));
        }
        finally
        {
            Environment.SetEnvironmentVariable(marker, null);
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// Node は <c>`${key}=${value}`</c> で環境変数を組み立てるので、<c>null</c> は空文字ではなく
    /// 文字列 <c>"null"</c> として子プロセスへ届く。スクリプトが <c>-z</c> で判定していると
    /// 空文字にした場合と結果が変わるため、そのまま写す。
    /// </summary>
    [Fact]
    public async Task NullValuesArriveAsTheLiteralStringNull()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        string directory = Path.Combine(Path.GetTempPath(), $"tnla-hook-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string outputPath = Path.Combine(directory, "out.txt");
        string script = CreateDumpScript(
            directory, outputPath, "PROGRAMID", "DESCRIPTION", "EXTENDED", "CHANNELTYPE");

        try
        {
            var runner = new CommandHookRunner(NullLogger<CommandHookRunner>.Instance);
            runner.RunReserveHook(script, new ReserveHookPayload(
                ReserveId: 7,
                ProgramId: null,
                ChannelId: 1,
                ChannelName: "CH",
                HalfWidthChannelName: "CH",
                StartAt: 0,
                EndAt: 0,
                Name: "N",
                HalfWidthName: "N"));

            IReadOnlyDictionary<string, string> variables = await ReadDumpAsync(outputPath);
            Assert.Equal("null", variables["PROGRAMID"]);
            Assert.Equal("null", variables["DESCRIPTION"]);
            Assert.Equal("null", variables["EXTENDED"]);
            Assert.Equal("null", variables["CHANNELTYPE"]);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// エンコード完了フックだけは、上流が <c>''</c> を入れている場所がある。
    /// 根拠: <c>createFinishEncodeCmd</c> の <c>VIDEOFILEID</c> と <c>DESCRIPTION</c> 系。
    /// </summary>
    [Fact]
    public async Task TheEncodeFinishHookUsesEmptyStringsWhereUpstreamDoes()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        string directory = Path.Combine(Path.GetTempPath(), $"tnla-hook-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string outputPath = Path.Combine(directory, "out.txt");
        string script = CreateDumpScript(
            directory, outputPath, "VIDEOFILEID", "OUTPUTPATH", "DESCRIPTION", "EXTENDED");

        try
        {
            var runner = new CommandHookRunner(NullLogger<CommandHookRunner>.Instance);
            runner.RunEncodeFinishHook(script, new EncodeFinishHookPayload(
                RecordedId: 5,
                VideoFileId: null,
                OutputPath: null,
                Mode: "H.264",
                ChannelId: 1,
                ChannelName: "CH",
                HalfWidthChannelName: "CH",
                Name: "N",
                HalfWidthName: "N"));

            IReadOnlyDictionary<string, string> variables = await ReadDumpAsync(outputPath);
            Assert.Equal(string.Empty, variables["VIDEOFILEID"]);
            Assert.Equal("null", variables["OUTPUTPATH"]);
            Assert.Equal(string.Empty, variables["DESCRIPTION"]);
            Assert.Equal(string.Empty, variables["EXTENDED"]);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void ANullOrBlankCommandDoesNothing()
    {
        // 何も起動しないことをそのまま確かめる術は無いので、例外にならず戻ってくることだけ確認する。
        var runner = new CommandHookRunner(NullLogger<CommandHookRunner>.Instance);
        runner.RunReserveHook(null, new ReserveHookPayload(1, null, 1, "CH", "CH", 0, 0, "N", "N"));
        runner.RunReserveHook("   ", new ReserveHookPayload(1, null, 1, "CH", "CH", 0, 0, "N", "N"));
    }

    [System.Runtime.Versioning.SupportedOSPlatform("linux")]
    private static string CreateDumpScript(string directory, string outputPath, params string[] keys)
    {
        string script = Path.Combine(directory, "dump-env");
        var lines = new List<string> { "#!/bin/sh" };
        lines.AddRange(keys.Select(key => $"""printf '{key}=%s\n' "${key}" >> "{outputPath}" """));
        File.WriteAllText(script, string.Join('\n', lines) + '\n');
        File.SetUnixFileMode(
            script,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return script;
    }

    private static async Task<IReadOnlyDictionary<string, string>> ReadDumpAsync(string outputPath)
    {
        long startedAt = Stopwatch.GetTimestamp();
        while (!File.Exists(outputPath))
        {
            if (Stopwatch.GetElapsedTime(startedAt) >= TimeSpan.FromSeconds(10))
            {
                throw new TimeoutException("The hook script did not run in time.");
            }

            await Task.Delay(20);
        }

        // fire-and-forget なので、書き込みの途中でファイルの存在だけ見えることがある。
        // 内容が安定するまで少し待つ。
        string? previous = null;
        for (int attempt = 0; attempt < 50; attempt++)
        {
            string current = await File.ReadAllTextAsync(outputPath);
            if (current == previous && current.Length > 0)
            {
                break;
            }

            previous = current;
            await Task.Delay(20);
        }

        return (previous ?? string.Empty)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Split('=', 2))
            .ToDictionary(parts => parts[0], parts => parts.Length > 1 ? parts[1] : string.Empty);
    }
}
