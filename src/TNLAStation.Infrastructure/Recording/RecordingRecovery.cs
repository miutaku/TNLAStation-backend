using Microsoft.Extensions.Logging;
using TNLAStation.Application.Abstractions;

namespace TNLAStation.Infrastructure.Recording;

/// <summary>
/// 前回落ちたときに書きかけだった録画を畳む。
///
/// 録画中のままにしておくと、いつまでも「録画中」に出続け、同じ番組を録り直すこともできない。
/// 書けていればそのまま録画として残し、1 バイトも書けていなければ無かったことにする。落ちた
/// 瞬間まで録れた分は、途中で切れていても再生できるので捨てない。
/// </summary>
public sealed partial class RecordingRecovery(IRecordingStore store, ILogger<RecordingRecovery> logger)
{
    public async Task RecoverAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<UnfinishedRecording> unfinished = await store.ListUnfinishedAsync(cancellationToken);
        foreach (UnfinishedRecording item in unfinished)
        {
            var file = new FileInfo(Path.Combine(item.ParentDirectoryName, item.Filename));
            if (file.Exists && file.Length > 0)
            {
                await store.CompleteAsync(item.RecordedId, item.VideoFileId, file.Length, cancellationToken);
                LogRecoveredRecording(logger, item.Filename, file.Length);
            }
            else
            {
                await store.AbortAsync(item.RecordedId, cancellationToken);
                LogAbandonedRecording(logger, item.Filename);
            }
        }
    }

    [LoggerMessage(
        EventId = 4020,
        Level = LogLevel.Information,
        Message = "Closed the recording {FileName} left behind by a previous run ({Size} bytes)")]
    private static partial void LogRecoveredRecording(ILogger logger, string fileName, long size);

    [LoggerMessage(
        EventId = 4021,
        Level = LogLevel.Information,
        Message = "Discarded the empty recording {FileName} left behind by a previous run")]
    private static partial void LogAbandonedRecording(ILogger logger, string fileName);
}
