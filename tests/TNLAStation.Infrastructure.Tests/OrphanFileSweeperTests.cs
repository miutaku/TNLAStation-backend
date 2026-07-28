using TNLAStation.Infrastructure.Repositories;

namespace TNLAStation.Infrastructure.Tests;

/// <summary>
/// <see cref="OrphanFileSweeper"/> は録画ファイルを実際に消す処理なので、Postgres 抜きでも
/// 直接、確実に試験できるようにしてある。守るべきものを誤って消さないことを最優先で確かめる。
/// </summary>
public sealed class OrphanFileSweeperTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), $"tnla-sweep-{Guid.NewGuid():N}");
    private readonly string outsideDirectory = Path.Combine(Path.GetTempPath(), $"tnla-sweep-outside-{Guid.NewGuid():N}");

    [Fact]
    public async Task AFileNotInTheKnownSetIsDeleted()
    {
        Directory.CreateDirectory(root);
        string orphan = Path.Combine(root, "orphan.ts");
        await File.WriteAllBytesAsync(orphan, [1]);

        int removed = OrphanFileSweeper.Sweep([root], new HashSet<string>(StringComparer.Ordinal));

        Assert.Equal(1, removed);
        Assert.False(File.Exists(orphan));
    }

    [Fact]
    public async Task AFileInTheKnownSetSurvives()
    {
        Directory.CreateDirectory(root);
        string kept = Path.Combine(root, "kept.ts");
        await File.WriteAllBytesAsync(kept, [1]);

        int removed = OrphanFileSweeper.Sweep([root], new HashSet<string>(StringComparer.Ordinal) { Path.GetFullPath(kept) });

        Assert.Equal(0, removed);
        Assert.True(File.Exists(kept));
    }

    [Fact]
    public async Task AFileOutsideEveryConfiguredRootIsNeverTouched()
    {
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(outsideDirectory);
        string outsideFile = Path.Combine(outsideDirectory, "unrelated.ts");
        await File.WriteAllBytesAsync(outsideFile, [1]);

        // root だけを対象にする。outsideDirectory は一覧に無いので触れられない。
        int removed = OrphanFileSweeper.Sweep([root], new HashSet<string>(StringComparer.Ordinal));

        Assert.Equal(0, removed);
        Assert.True(File.Exists(outsideFile));
    }

    [Fact]
    public void EmptySubdirectoriesAreRemovedButTheRootItselfSurvives()
    {
        Directory.CreateDirectory(root);
        string nested = Path.Combine(root, "a", "b");
        Directory.CreateDirectory(nested);

        OrphanFileSweeper.Sweep([root], new HashSet<string>(StringComparer.Ordinal));

        Assert.False(Directory.Exists(nested));
        Assert.False(Directory.Exists(Path.Combine(root, "a")));
        Assert.True(Directory.Exists(root));
    }

    [Fact]
    public async Task ADirectoryThatStillHoldsAKnownFileIsNotRemoved()
    {
        Directory.CreateDirectory(root);
        string subdirectory = Path.Combine(root, "sub");
        Directory.CreateDirectory(subdirectory);
        string kept = Path.Combine(subdirectory, "kept.ts");
        await File.WriteAllBytesAsync(kept, [1]);

        OrphanFileSweeper.Sweep([root], new HashSet<string>(StringComparer.Ordinal) { Path.GetFullPath(kept) });

        Assert.True(File.Exists(kept));
        Assert.True(Directory.Exists(subdirectory));
    }

    [Fact]
    public void ANonExistentRootIsSkippedWithoutThrowing()
    {
        int removed = OrphanFileSweeper.Sweep(
            [Path.Combine(Path.GetTempPath(), $"tnla-sweep-missing-{Guid.NewGuid():N}")],
            new HashSet<string>(StringComparer.Ordinal));

        Assert.Equal(0, removed);
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }

        if (Directory.Exists(outsideDirectory))
        {
            Directory.Delete(outsideDirectory, recursive: true);
        }
    }
}
