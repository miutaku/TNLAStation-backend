using System.Globalization;
using System.Text;

namespace TNLAStation.Infrastructure.Recording;

/// <summary>
/// 保存するファイル名を決める。後から人が探すものなので、日時と番組名で読める形にする。
/// </summary>
internal static class RecordingFileName
{
    private static readonly TimeZoneInfo JapanTime = ResolveJapanTime();

    public static string Create(DateTimeOffset startAt, string channelName, string programName)
    {
        DateTimeOffset local = TimeZoneInfo.ConvertTime(startAt, JapanTime);
        var builder = new StringBuilder();
        builder.Append(local.ToString("yyyyMMdd-HHmm", CultureInfo.InvariantCulture));
        builder.Append('-');
        builder.Append(Sanitize(channelName));
        builder.Append('-');
        builder.Append(Sanitize(programName));

        // 長すぎる名前はファイルシステムが受け付けない。拡張子の分を残して切る。
        const int maxLength = 200;
        if (builder.Length > maxLength)
        {
            builder.Length = maxLength;
        }

        builder.Append(".ts");
        return builder.ToString();
    }

    /// <summary>
    /// 同じ時刻に同じ名前の番組が来ることがある。上書きしてしまうと、先に録ったほうが消える。
    /// </summary>
    public static string EnsureUnique(string directory, string filename)
    {
        string baseName = Path.GetFileNameWithoutExtension(filename);
        string extension = Path.GetExtension(filename);
        string candidate = filename;
        for (int suffix = 1; File.Exists(Path.Combine(directory, candidate)); suffix++)
        {
            candidate = $"{baseName}-{suffix.ToString(CultureInfo.InvariantCulture)}{extension}";
        }

        return candidate;
    }

    private static string Sanitize(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (char character in value)
        {
            // パス区切りと制御文字だけを落とす。全角記号まで削ると番組名が読めなくなる。
            builder.Append(character switch
            {
                '/' or '\\' or ':' or '*' or '?' or '"' or '<' or '>' or '|' => '_',
                _ when char.IsControl(character) => '_',
                _ => character,
            });
        }

        return builder.ToString().Trim();
    }

    private static TimeZoneInfo ResolveJapanTime()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Asia/Tokyo");
        }
        catch (TimeZoneNotFoundException)
        {
            // タイムゾーン情報を持たない構成でも、名前が UTC 基準になるだけで録画は続けられる。
            return TimeZoneInfo.Utc;
        }
    }
}
