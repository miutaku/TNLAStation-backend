using System.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TNLAStation.Application.Abstractions;
using TNLAStation.Application.Models;
using TNLAStation.Domain;
using TNLAStation.Infrastructure.CommandHooks;
using TNLAStation.Infrastructure.Configuration;
using TNLAStation.Infrastructure.Mirakurun;
using TNLAStation.Infrastructure.Repositories;
using TNLAStation.Infrastructure.Reserves;

namespace TNLAStation.Infrastructure.Tests;

/// <summary>
/// 予約は生成のたびに丸ごと作り直されるので (行 ID は毎回変わる)、新規・更新・削除の
/// フックは安定キーでの突き合わせに頼っている。実 DB とルールで実際に生成させて確かめる。
/// </summary>
public sealed class ReserveGeneratorHookTests
{
    [PostgresFact]
    public async Task RuleMatchesFireNewThenUpdateThenDeleteHooksAsTheProgramChanges()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
        string directory = Path.Combine(Path.GetTempPath(), $"tnla-reserve-hooks-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string addedPath = Path.Combine(directory, "added.txt");
        string updatedPath = Path.Combine(directory, "updated.txt");
        string deletedPath = Path.Combine(directory, "deleted.txt");

        try
        {
            var clock = RecordingTestData.Clock();
            var rules = new PostgresRuleRepository(database.ContextFactory);
            long ruleId = await rules.AddAsync(
                new RecordingRule(
                    0,
                    IsTimeSpecification: false,
                    new EpgSearchQuery(Keyword: "テスト", Name: true, Gr: true),
                    new RuleReserveOption(Enable: true, AllowEndLack: true, AvoidDuplicate: false)),
                CancellationToken.None);

            var reserves = new PostgresReserveRepository(database.ContextFactory, clock);
            var generator = new ReserveGenerator(
                new PostgresEpgRepository(database.ContextFactory, Options.Create(new EpgOptions()), clock),
                rules,
                new PostgresRecordedHistoryStore(database.ContextFactory),
                reserves,
                new TunerOnlyMirakurun(),
                new CommandHookRunner(NullLogger<CommandHookRunner>.Instance),
                Options.Create(new ReserveOptions()),
                Options.Create(new CommandHookOptions
                {
                    ReserveNewAdditionCommand = DumpScript(directory, "added", addedPath),
                    ReserveUpdateCommand = DumpScript(directory, "updated", updatedPath),
                    ReserveDeletedCommand = DumpScript(directory, "deleted", deletedPath),
                }),
                new NoopScheduleTrigger(),
                NullClientNotifier.Instance,
                clock,
                NullLogger<ReserveGenerator>.Instance);

            await RecordingTestData.SeedEpgAsync(database, RecordingTestData.CreateProgram(name: "テスト番組"));
            await generator.RunAsync(CancellationToken.None);

            IReadOnlyDictionary<string, string> added = await ReadDumpAsync(addedPath);
            Assert.Equal("テスト番組", added["NAME"]);
            AssertAbsent(updatedPath);
            AssertAbsent(deletedPath);

            File.Delete(addedPath);
            await RecordingTestData.SeedEpgAsync(database, RecordingTestData.CreateProgram(name: "テスト番組2"));
            await generator.RunAsync(CancellationToken.None);

            IReadOnlyDictionary<string, string> updated = await ReadDumpAsync(updatedPath);
            Assert.Equal("テスト番組2", updated["NAME"]);
            AssertAbsent(addedPath);

            File.Delete(updatedPath);
            await RecordingTestData.SeedEpgAsync(database);
            await generator.RunAsync(CancellationToken.None);

            IReadOnlyDictionary<string, string> deleted = await ReadDumpAsync(deletedPath);
            Assert.Equal("テスト番組2", deleted["NAME"]);
            AssertAbsent(addedPath);
            AssertAbsent(updatedPath);

            _ = ruleId;
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [System.Runtime.Versioning.SupportedOSPlatform("linux")]
    private static string DumpScript(string directory, string label, string outputPath)
    {
        string script = Path.Combine(directory, $"dump-{label}");
        File.WriteAllText(
            script,
            $"""
            #!/bin/sh
            printf 'RESERVEID=%s\n' "$RESERVEID" >> "{outputPath}"
            printf 'NAME=%s\n' "$NAME" >> "{outputPath}"
            """ + '\n');
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
                throw new TimeoutException($"The hook script did not run in time: {outputPath}");
            }

            await Task.Delay(20);
        }

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

    private static void AssertAbsent(string outputPath) =>
        Assert.False(File.Exists(outputPath), $"Expected {outputPath} not to have been written.");

    private sealed class NoopScheduleTrigger : IRecordingScheduleTrigger
    {
        public ValueTask RequestAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;
    }

    private sealed class TunerOnlyMirakurun : IMirakurunClient
    {
        public ValueTask<Stream> OpenServiceStreamAsync(long channelId, CancellationToken cancellationToken, int? priority = null) =>
            throw new NotSupportedException();

        public ValueTask<IReadOnlyList<MirakurunServiceDto>> GetServicesAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public ValueTask<IReadOnlyList<MirakurunProgramDto>> GetProgramsAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<MirakurunEventDto> ReadEventsAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public ValueTask<IReadOnlyList<MirakurunTunerDto>> GetTunersAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<MirakurunTunerDto>>(
            [
                new() { Index = 0, Name = "GR tuner", Types = ["GR"], IsAvailable = true, IsFault = false },
            ]);
    }
}
