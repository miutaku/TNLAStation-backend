using System.Globalization;
using System.Text;
using TNLAStation.Domain;

namespace TNLAStation.Infrastructure.Recording;

/// <summary>
/// 保存するファイル名を決める。後から人が探すものなので、日時と番組名で読める形にする。
///
/// テンプレートは EPGStation の <c>recordedFormat</c> に倣ったプレースホルダ置換で、
/// %YEAR% %SHORTYEAR% %MONTH% %DAY% %HOUR% %MIN% %SEC% %DOW% %TYPE% %CHID% %CHNAME%
/// %HALF_WIDTH_CHNAME% %CH% %SID% %ID% %TITLE% %HALF_WIDTH_TITLE% を差し替える。
/// </summary>
internal static class RecordingFileName
{
    private static readonly TimeZoneInfo JapanTime = ResolveJapanTime();

    /// <summary>EPGStation と同じく、日曜始まりの漢字 1 文字。DayOfWeek の数値 (0=日) と対応する。</summary>
    private static readonly string[] DayOfWeekKanji = ["日", "月", "火", "水", "木", "金", "土"];

    public static string Create(
        string template,
        string extension,
        DateTimeOffset startAt,
        EpgChannel? channel,
        Reservation reserve)
    {
        DateTimeOffset local = TimeZoneInfo.ConvertTime(startAt, JapanTime);
        string result = template
            .Replace("%YEAR%", local.ToString("yyyy", CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("%SHORTYEAR%", local.ToString("yy", CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("%MONTH%", local.ToString("MM", CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("%DAY%", local.ToString("dd", CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("%HOUR%", local.ToString("HH", CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("%MIN%", local.ToString("mm", CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("%SEC%", local.ToString("ss", CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("%DOW%", DayOfWeekKanji[(int)local.DayOfWeek], StringComparison.Ordinal)
            .Replace("%TYPE%", channel?.ChannelType ?? "NULL", StringComparison.Ordinal)
            .Replace("%CHID%", reserve.ChannelId.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("%CHNAME%", channel?.Name ?? "CH", StringComparison.Ordinal)
            .Replace("%HALF_WIDTH_CHNAME%", channel?.HalfWidthName ?? "CH", StringComparison.Ordinal)
            .Replace("%CH%", channel?.Channel ?? "NULL", StringComparison.Ordinal)
            .Replace("%SID%", channel is null ? "NULL" : channel.ServiceId.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("%ID%", reserve.Id.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("%TITLE%", reserve.Name, StringComparison.Ordinal)
            .Replace("%HALF_WIDTH_TITLE%", reserve.HalfWidthName, StringComparison.Ordinal);

        string sanitized = Sanitize(result);

        // 長すぎる名前はファイルシステムが受け付けない。拡張子の分を残して切る。
        const int maxLength = 200;
        if (sanitized.Length > maxLength)
        {
            sanitized = sanitized[..maxLength];
        }

        return sanitized + extension;
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
