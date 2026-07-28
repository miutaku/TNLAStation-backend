using System.Globalization;

namespace TNLAStation.FfmpegWorker.Streaming;

/// <summary>
/// HLS 用 ffmpeg 引数の組み立て。backend の HlsStreamManager が持っていたものと同一の
/// コマンドラインを、ここでは channel/DB の知識なしに組み立てられる形にして移設した。
/// </summary>
internal static class HlsArguments
{
    /// <summary>
    /// ライブ視聴。Mirakurun から届く MPEG-TS を標準入力から受け取り、HLS へ作り替える。
    /// </summary>
    public static string[] CreateLive(string workDirectory, long streamId, int height, string videoBitrate, string audioBitrate, int segmentSeconds)
    {
        string segmentSecondsText = segmentSeconds.ToString(CultureInfo.InvariantCulture);
        string playlist = Path.Combine(workDirectory, $"stream{streamId}.m3u8");
        string segments = Path.Combine(workDirectory, $"stream{streamId}-%d.ts");

        return
        [
            "-hide_banner",
            "-loglevel", "warning",
            // 放送波は欠けた TS が混ざる。落とさずに読み飛ばす。
            "-fflags", "+discardcorrupt",
            "-analyzeduration", "10M",
            "-probesize", "32M",
            // 副音声つきの番組で、主音声だけを取り出す。
            "-dual_mono_mode", "main",
            "-i", "pipe:0",
            "-map", "0:v:0",
            "-map", "0:a:0",
            "-ignore_unknown",
            "-sn",
            "-dn",
            "-c:v", "libx264",
            "-preset", "veryfast",
            "-profile:v", "main",
            // インターレース解除を挟まないと、動きのある場面が櫛状に割れる。
            "-vf", $"yadif,scale=-2:{height.ToString(CultureInfo.InvariantCulture)}",
            "-b:v", videoBitrate,
            "-maxrate", videoBitrate,
            "-bufsize", videoBitrate,
            // セグメントの先頭は必ずキーフレームにする。でないと切り替えで映像が崩れる。
            "-force_key_frames", $"expr:gte(t,n_forced*{segmentSecondsText})",
            "-c:a", "aac",
            "-ar", "48000",
            "-b:a", audioBitrate,
            "-ac", "2",
            "-max_muxing_queue_size", "1024",
            "-f", "hls",
            "-hls_time", segmentSecondsText,
            "-hls_list_size", "6",
            "-hls_delete_threshold", "1",
            "-hls_flags", "delete_segments+temp_file+omit_endlist",
            "-hls_segment_filename", segments,
            "-y", playlist,
        ];
    }

    /// <summary>
    /// 録画済みは ffmpeg にファイルを直接読ませる。頭出しは入力の前に置く。後ろに置くと、
    /// 指定した位置まで復号してから捨てることになり、長い録画では待たされる。
    /// </summary>
    public static string[] CreateRecorded(string workDirectory, long streamId, int height, string videoBitrate, string audioBitrate, int segmentSeconds, string path, double playPosition)
    {
        string segmentSecondsText = segmentSeconds.ToString(CultureInfo.InvariantCulture);
        string playlist = Path.Combine(workDirectory, $"stream{streamId}.m3u8");
        string segments = Path.Combine(workDirectory, $"stream{streamId}-%d.ts");

        return
        [
            "-hide_banner",
            "-loglevel", "warning",
            "-fflags", "+discardcorrupt",
            "-analyzeduration", "10M",
            "-probesize", "32M",
            "-ss", playPosition.ToString("0.###", CultureInfo.InvariantCulture),
            "-dual_mono_mode", "main",
            "-i", path,
            "-map", "0:v:0",
            "-map", "0:a:0",
            "-ignore_unknown",
            "-sn",
            "-dn",
            "-c:v", "libx264",
            "-preset", "veryfast",
            "-profile:v", "main",
            "-vf", $"yadif,scale=-2:{height.ToString(CultureInfo.InvariantCulture)}",
            "-b:v", videoBitrate,
            "-maxrate", videoBitrate,
            "-bufsize", videoBitrate,
            "-force_key_frames", $"expr:gte(t,n_forced*{segmentSecondsText})",
            "-c:a", "aac",
            "-ar", "48000",
            "-b:a", audioBitrate,
            "-ac", "2",
            "-max_muxing_queue_size", "1024",
            "-f", "hls",
            "-hls_time", segmentSecondsText,
            // 録画済みは頭から順に見るので、過ぎたセグメントも残す。巻き戻せなくなる。
            "-hls_list_size", "0",
            "-hls_flags", "temp_file",
            "-hls_segment_filename", segments,
            "-y", playlist,
        ];
    }

    public static string[] CreateTranscode(string input, bool isLiveInput, int height, string videoBitrate, string audioBitrate, IReadOnlyList<string> formatArguments, double? playPosition)
    {
        var arguments = new List<string>
        {
            "-hide_banner",
            "-loglevel", "error",
            "-fflags", "+discardcorrupt",
        };

        if (isLiveInput)
        {
            // 流れてくる先を戻して読み直せないので、ffmpeg は形式の判別に失敗することがある。
            // 失敗すると 1 バイトも出さずに待ち続けるので、放送波だと分かっている以上は明示する。
            arguments.AddRange(["-f", "mpegts"]);
        }

        arguments.AddRange(["-analyzeduration", "10M", "-probesize", "32M"]);

        if (playPosition is { } position and > 0)
        {
            arguments.Add("-ss");
            arguments.Add(position.ToString("0.###", CultureInfo.InvariantCulture));
        }

        arguments.AddRange(["-dual_mono_mode", "main", "-i", input]);
        arguments.AddRange(["-map", "0:v:0", "-map", "0:a:0", "-ignore_unknown", "-sn", "-dn"]);
        arguments.AddRange([
            "-vf", $"yadif,scale=-2:{height.ToString(CultureInfo.InvariantCulture)}",
            "-b:v", videoBitrate,
            "-maxrate", videoBitrate,
            "-bufsize", videoBitrate,
            "-b:a", audioBitrate,
            "-ar", "48000",
            "-ac", "2",
        ]);
        arguments.AddRange(formatArguments);
        arguments.Add("pipe:1");
        return [.. arguments];
    }
}
