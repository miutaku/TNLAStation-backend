namespace TNLAStation.Infrastructure.Repositories;

/// <summary>
/// 保存先の下にあるが、渡された「既知のファイル」一覧に無いものを消す実処理。DB を持たない
/// 純粋なファイル操作として切り出してあるので、実データベース無しでも安全性 (何を消して
/// 何を残すか) を直接試験できる。
/// </summary>
internal static class OrphanFileSweeper
{
    /// <param name="roots">掃除の対象にする保存先のフルパス。この配下だけを見る。</param>
    /// <param name="knownFullPaths">残すべきファイルのフルパス一式 (VideoFiles・DropLogFiles など)。</param>
    /// <returns>削除したファイル数。</returns>
    public static int Sweep(IReadOnlyList<string> roots, IReadOnlySet<string> knownFullPaths)
    {
        int removed = 0;
        foreach (string root in roots)
        {
            if (!Directory.Exists(root))
            {
                continue;
            }

            foreach (string file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            {
                if (knownFullPaths.Contains(Path.GetFullPath(file)))
                {
                    continue;
                }

                try
                {
                    File.Delete(file);
                    removed++;
                }
                catch (IOException)
                {
                    // 消せなくても他のファイルの片付けは続ける。
                }
                catch (UnauthorizedAccessException)
                {
                }
            }

            IOrderedEnumerable<string> directories = Directory
                .EnumerateDirectories(root, "*", SearchOption.AllDirectories)
                .OrderByDescending(directory => directory.Length);
            foreach (string directory in directories)
            {
                try
                {
                    if (Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory).Any())
                    {
                        Directory.Delete(directory);
                    }
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }

        return removed;
    }
}
