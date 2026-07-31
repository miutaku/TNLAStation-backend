namespace TNLAStation.Infrastructure;

/// <summary>
/// 空白区切りの単純なコマンド分割。二重引用符で囲めば空白を含む引数も渡せる。
/// シェルは介さないので、パイプやリダイレクトはそのままの文字として渡る。
/// </summary>
internal static class ShellCommandLine
{
    /// <summary>
    /// 区切りは半角の空白と制御文字だけ。char.IsWhiteSpace は全角空白 (U+3000) も真になり、
    /// 番組名に全角空白を含む録画ファイルのパスがそこで切れる。
    /// </summary>
    private static bool IsSeparator(char character) => character is ' ' or '\t' or '\r' or '\n';

    public static string[] Split(string command)
    {
        var parts = new List<string>();
        var current = new System.Text.StringBuilder();
        bool inQuotes = false;
        foreach (char character in command)
        {
            if (character == '"')
            {
                inQuotes = !inQuotes;
                continue;
            }

            if (char.IsWhiteSpace(character) && !inQuotes)
            {
                if (current.Length > 0)
                {
                    parts.Add(current.ToString());
                    current.Clear();
                }

                continue;
            }

            current.Append(character);
        }

        if (current.Length > 0)
        {
            parts.Add(current.ToString());
        }

        return [.. parts];
    }
}
