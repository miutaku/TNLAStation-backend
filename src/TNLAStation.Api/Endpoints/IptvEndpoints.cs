using System.Globalization;
using System.Text;
using System.Xml;
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
        // EPGStation は schema に default: true と書きながら、実装では未指定を undefined のまま
        // 比較するため全角のまま返る (isHalfWidth === true が偽になる)。実装側に合わせる。
        [FromQuery] bool isHalfWidth = false,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<EpgChannel> channels = await repository.ListChannelsAsync(cancellationToken);
        string origin = Origin(context);
        var builder = new StringBuilder("#EXTM3U\n");

        foreach (EpgChannel channel in channels)
        {
            if (channel.ServiceType is { } serviceType && !MediaServiceTypes.Contains(serviceType))
            {
                continue;
            }

            string id = channel.Id.ToString(CultureInfo.InvariantCulture);
            string name = isHalfWidth ? channel.HalfWidthName : channel.Name;
            string logo = channel.HasLogoData ? $"tvg-logo=\"{origin}/api/channels/{id}/logo\" " : string.Empty;
            builder.Append("#KODIPROP:mimetype=video/mp2t\n");
            builder.Append(CultureInfo.InvariantCulture, $"#EXTINF:-1 tvg-id=\"{id}\" ");
            builder.Append(logo);
            builder.Append(CultureInfo.InvariantCulture, $"group-title=\"{channel.ChannelType}\",{name}\n");
            builder.Append(CultureInfo.InvariantCulture,
                $"{origin}/api/streams/live/{id}/m2ts?mode={mode.ToString(CultureInfo.InvariantCulture)}\n");
        }

        return Results.Text(builder.ToString(), "application/x-mpegURL");
    }

    private static async Task<IResult> GetEpgAsync(
        IEpgRepository repository,
        TimeProvider timeProvider,
        [FromQuery] int days = 3,
        [FromQuery] bool isHalfWidth = false,
        CancellationToken cancellationToken = default)
    {
        DateTimeOffset now = timeProvider.GetUtcNow();
        IReadOnlyList<EpgChannel> channels = await repository.ListChannelsAsync(cancellationToken);
        IReadOnlyList<EpgProgram> programs = await repository.FindProgramsAsync(
            new EpgScheduleQuery(now, now.AddDays(days), ["GR", "BS", "CS", "SKY"]),
            cancellationToken);
        // EPGStation は番組が 1 つも無いチャンネルを channel 要素ごと省く。
        HashSet<long> channelIdsWithPrograms = [.. programs.Select(program => program.ChannelId)];
        channels = [.. channels.Where(channel => channelIdsWithPrograms.Contains(channel.Id))];

        var settings = new XmlWriterSettings
        {
            Async = true,
            Encoding = new UTF8Encoding(false),
            Indent = false,
            OmitXmlDeclaration = true,
        };
        // StringWriter へ書くと、宣言が utf-16 になる。宣言を信じて読む取り込み側が
        // 文字化けするので、最初から UTF-8 の byte 列として組み立てる。
        var output = new MemoryStream();
        await output.WriteAsync("<?xml version=\"1.0\" encoding=\"UTF-8\"?>"u8.ToArray(), cancellationToken);
        await using (XmlWriter writer = XmlWriter.Create(output, settings))
        {
            await writer.WriteDocTypeAsync("tv", null, "xmltv.dtd", null);
            await writer.WriteStartElementAsync(null, "tv", null);
            await writer.WriteAttributeStringAsync(null, "generator-info-name", null, "EPGStation");

            foreach (EpgChannel channel in channels)
            {
                await writer.WriteStartElementAsync(null, "channel", null);
                await writer.WriteAttributeStringAsync(
                    null,
                    "id",
                    null,
                    channel.Id.ToString(CultureInfo.InvariantCulture));
                await writer.WriteAttributeStringAsync(null, "tp", null, channel.Channel);
                await WriteTextElementAsync(writer, "display-name", isHalfWidth ? channel.HalfWidthName : channel.Name);
                await WriteTextElementAsync(
                    writer,
                    "service_id",
                    channel.ServiceId.ToString(CultureInfo.InvariantCulture),
                    withLanguage: false);
                await writer.WriteEndElementAsync();
            }

            foreach (EpgProgram program in programs)
            {
                await writer.WriteStartElementAsync(null, "programme", null);
                await writer.WriteAttributeStringAsync(null, "start", null, FormatTime(program.StartAt));
                await writer.WriteAttributeStringAsync(null, "stop", null, FormatTime(program.EndAt));
                await writer.WriteAttributeStringAsync(
                    null,
                    "channel",
                    null,
                    program.ChannelId.ToString(CultureInfo.InvariantCulture));
                await WriteTextElementAsync(writer, "title", isHalfWidth ? program.HalfWidthName : program.Name);

                string? description = isHalfWidth ? program.HalfWidthDescription : program.Description;
                string? extended = isHalfWidth ? program.HalfWidthExtended : program.Extended;
                string combinedDescription = $"{description}{extended}";
                if (!string.IsNullOrWhiteSpace(combinedDescription))
                {
                    await WriteTextElementAsync(writer, "desc", combinedDescription);
                }

                await writer.WriteEndElementAsync();
            }

            await writer.WriteEndElementAsync();
            await writer.FlushAsync();
        }

        return Results.File(output.ToArray(), "application/xml; charset=utf-8");
    }

    private static async Task WriteTextElementAsync(
        XmlWriter writer,
        string name,
        string value,
        bool withLanguage = true)
    {
        await writer.WriteStartElementAsync(null, name, null);
        if (withLanguage)
        {
            // 取り込む側は言語で表示を選ぶ。付けないと選べない。
            await writer.WriteAttributeStringAsync(null, "lang", null, "ja_JP");
        }

        await writer.WriteStringAsync(value);
        await writer.WriteEndElementAsync();
    }

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
