using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using TNLAStation.Api.Contracts;
using TNLAStation.Application.Abstractions;
using TNLAStation.Application.Models;
using TNLAStation.Domain;

namespace TNLAStation.Api.Endpoints;

/// <summary>
/// テレビ用の再生機へ渡す一覧と番組表。取り込む側は決まった形しか読まないので、
/// EPGStation と同じ書きかたに合わせる。
/// </summary>
internal static class IptvEndpoints
{
    public static IEndpointRouteBuilder MapIptvEndpoints(this IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder iptv = endpoints.MapGroup("/api/iptv");

        iptv.MapGet("/channel.m3u8", GetChannelListAsync)
            .WithName("GetIptvChannelList")
            .WithSummary("IPTV チャンネル一覧")
            .WithTags("iptv");

        iptv.MapGet("/epg.xml", GetEpgAsync)
            .WithName("GetIptvEpg")
            .WithSummary("IPTV 番組表")
            .WithTags("iptv");

        return endpoints;
    }

    /// <summary>
    /// Mirakurun の serviceType のうち、映像・音声サービス扱いのもの (ChannelUtil.isMediaService)。
    /// データ放送専用サービスなどを除く。
    /// </summary>
    private static readonly HashSet<int> MediaServiceTypes = [0x01, 0x02, 0xa1, 0xa2, 0xa5, 0xa6, 0xad];

    private static async Task<IResult> GetChannelListAsync(
        HttpContext context,
        IEpgRepository repository,
        [FromQuery] int mode,
        [FromQuery] bool? isHalfWidth = null,
        CancellationToken cancellationToken = default)
    {
        bool halfWidthNames = isHalfWidth ?? ChannelNameDefaultsToHalfWidth;
        IReadOnlyList<EpgChannel> channels = await repository.ListChannelsAsync(cancellationToken);
        string origin = Origin(context);
        var builder = new StringBuilder("#EXTM3U\n");
        // 同名の放送局は取り込む側が同じものと見なす。EPGStation は 2 つめ以降へ半角空白を
        // 足して別物にしている。
        var seenNames = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (EpgChannel channel in channels)
        {
            if (channel.ServiceType is { } serviceType && !MediaServiceTypes.Contains(serviceType))
            {
                continue;
            }

            string id = channel.Id.ToString(CultureInfo.InvariantCulture);
            string name = halfWidthNames ? channel.HalfWidthName : channel.Name;
            if (seenNames.TryGetValue(name, out int seen))
            {
                seenNames[name] = seen + 1;
                name += new string(' ', seen + 2);
            }
            else
            {
                seenNames[name] = 0;
            }

            string logo = channel.HasLogoData ? $"tvg-logo=\"{origin}/api/channels/{id}/logo\"" : string.Empty;
            builder.Append("#KODIPROP:mimetype=video/mp2t\n");
            builder.Append(CultureInfo.InvariantCulture, $"#EXTINF:-1 tvg-id=\"{id}\" {logo} ");
            // 末尾の全角空白は EPGStation がそのまま出しているもの。取り込む側は名前で
            // 突き合わせることがあり、有無が食い違うと別の放送局として扱われる。
            builder.Append(CultureInfo.InvariantCulture, $"group-title=\"{channel.ChannelType}\",{name}\u3000\n");
            builder.Append(CultureInfo.InvariantCulture,
                $"{origin}/api/streams/live/{id}/m2ts?mode={mode.ToString(CultureInfo.InvariantCulture)}\n");
        }

        return Results.Text(builder.ToString(), "application/x-mpegURL");
    }

    private static async Task<IResult> GetEpgAsync(
        IEpgRepository repository,
        TimeProvider timeProvider,
        [FromQuery] int days = 3,
        [FromQuery] bool? isHalfWidth = null,
        CancellationToken cancellationToken = default)
    {
        bool halfWidthNames = isHalfWidth ?? ChannelNameDefaultsToHalfWidth;
        bool halfWidthPrograms = isHalfWidth ?? ProgramTextDefaultsToHalfWidth;
        DateTimeOffset now = timeProvider.GetUtcNow();
        IReadOnlyList<EpgChannel> channels = await repository.ListChannelsAsync(cancellationToken);
        IReadOnlyList<EpgProgram> programs = await repository.FindProgramsAsync(
            new EpgScheduleQuery(now, now.AddDays(days), ["GR", "BS", "CS", "SKY"]),
            cancellationToken);
        // EPGStation は番組が 1 つも無いチャンネルを channel 要素ごと省く。
        HashSet<long> channelIdsWithPrograms = [.. programs.Select(program => program.ChannelId)];
        channels = [.. channels.Where(channel => channelIdsWithPrograms.Contains(channel.Id))];

        // EPGStation は XmlWriter を使わず文字列を組み立て、禁止文字を全角へ置き換えて
        // 実体参照を一切出さない。取り込む側の実装差が出ないよう、その出力へ揃える。
        var programsByChannel = programs
            .GroupBy(program => program.ChannelId)
            .ToDictionary(group => group.Key, group => group.ToArray());

        var builder = new StringBuilder(
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" +
            "<!DOCTYPE tv SYSTEM \"xmltv.dtd\">" +
            "<tv generator-info-name=\"EPGStation\">");

        foreach (EpgChannel channel in channels)
        {
            if (!programsByChannel.TryGetValue(channel.Id, out EpgProgram[]? channelPrograms))
            {
                continue;
            }

            builder.Append(CultureInfo.InvariantCulture,
                $"<channel id=\"{channel.Id.ToString(CultureInfo.InvariantCulture)}\" tp=\"{channel.Channel}\">");
            builder.Append(CultureInfo.InvariantCulture,
                $"<display-name lang=\"ja_JP\">{(halfWidthNames ? channel.HalfWidthName : channel.Name)}</display-name>");
            builder.Append(CultureInfo.InvariantCulture,
                $"<service_id>{channel.ServiceId.ToString(CultureInfo.InvariantCulture)}</service_id>");
            builder.Append("</channel>\n");

            foreach (EpgProgram program in channelPrograms)
            {
                builder.Append(CultureInfo.InvariantCulture,
                    $"<programme start=\"{FormatTime(program.StartAt)}\" stop=\"{FormatTime(program.EndAt)}\" channel=\"{program.ChannelId.ToString(CultureInfo.InvariantCulture)}\">");
                builder.Append(CultureInfo.InvariantCulture,
                    $"<title lang=\"ja_JP\">{Sanitise(halfWidthPrograms ? program.HalfWidthName : program.Name)}</title>");

                string? description = halfWidthPrograms ? program.HalfWidthDescription : program.Description;
                if (description is not null)
                {
                    string? extended = halfWidthPrograms ? program.HalfWidthExtended : program.Extended;
                    builder.Append(CultureInfo.InvariantCulture,
                        $"    <desc lang=\"ja_JP\">{Sanitise(description)}{Sanitise(extended)}</desc>");
                }

                builder.Append("</programme>");
            }
        }

        builder.Append("</tv>");

        return Results.File(new UTF8Encoding(false).GetBytes(builder.ToString()), "application/xml; charset=\"UTF-8\"");
    }

    /// <summary>
    /// isHalfWidth を省いたときの既定。EPGStation は schema に default: true と書いているが、
    /// 実際の応答は放送局名だけ半角で、番組名と説明は全角のまま返す。取り込む側は同じ値を
    /// 見て突き合わせるので、この食い違いごと移植した。明示されたときは両方その値に従う。
    /// </summary>
    private const bool ChannelNameDefaultsToHalfWidth = true;

    private const bool ProgramTextDefaultsToHalfWidth = false;

    /// <summary>
    /// EPGStation の replaceStr。実体参照を出さずに済むよう、XML で使えない文字を全角へ
    /// 置き換える。取り込む側に実体参照を解けないものがあり、EPGStation はそれを避けている。
    /// </summary>
    private static string Sanitise(string? value) => value is null
        ? string.Empty
        : value
            .Replace("<", "＜", StringComparison.Ordinal)
            .Replace(">", "＞", StringComparison.Ordinal)
            .Replace("&", "＆", StringComparison.Ordinal)
            .Replace("\"", "”", StringComparison.Ordinal)
            .Replace("'", "’", StringComparison.Ordinal)
            .Replace("\u001a", string.Empty, StringComparison.Ordinal);


    /// <summary>
    /// XMLTV の時刻。取り込む側は現地時刻とずれ幅の組で読むので、UTC のままでは
    /// 放送時間が 9 時間ずれる。
    /// </summary>
    private static string FormatTime(DateTimeOffset value) =>
        TimeZoneInfo.ConvertTime(value, JapanTime).ToString("yyyyMMddHHmmss zzz", CultureInfo.InvariantCulture)
            .Replace(":", string.Empty, StringComparison.Ordinal);

    private static readonly TimeZoneInfo JapanTime = ResolveJapanTime();

    private static TimeZoneInfo ResolveJapanTime()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Asia/Tokyo");
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.Utc;
        }
    }

    private static string Origin(HttpContext context) =>
        $"{context.Request.Scheme}://{context.Request.Host}";
}
