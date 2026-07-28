namespace TNLAStation.FfmpegWorker;

/// <summary>
/// シェルを起動せず、EPGStation のコマンド文字列を実行ファイルと引数へ分割する。
/// 一重・二重引用符とバックスラッシュによるエスケープを扱う。
/// シェルは介さないので、パイプやリダイレクトはそのままの文字として渡る。
/// </summary>
internal static class ShellCommandLine
{
    public static string[] Split(string command)
    {
        var parts = new List<string>();
        var current = new System.Text.StringBuilder();
        char quote = '\0';
        bool escaped = false;
        foreach (char character in command)
        {
            if (escaped)
            {
                current.Append(character);
                escaped = false;
                continue;
            }

            if (character == '\\' && quote != '\'')
            {
                escaped = true;
                continue;
            }

            if ((character == '"' || character == '\'') && (quote == '\0' || quote == character))
            {
                quote = quote == '\0' ? character : '\0';
                continue;
            }

            if (char.IsWhiteSpace(character) && quote == '\0')
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

        if (escaped)
        {
            current.Append('\\');
        }

        if (quote != '\0')
        {
            throw new FormatException("Unterminated quote in command.");
        }

        if (current.Length > 0)
        {
            parts.Add(current.ToString());
        }

        return [.. parts];
    }
}
