using TNLAStation.Application.Abstractions;
using TNLAStation.Application.Models;
using TNLAStation.Domain;
using TNLAStation.Infrastructure.Mirakurun;

namespace TNLAStation.Infrastructure.Tests;

/// <summary>
/// 呼ばれない依存の埋め合わせ。粘りの試験は、鍵の取得や DB 接続で先に失敗するので、その先の
/// 依存には触れない。触れたら試験の前提が崩れているということなので、全部の口が例外を投げる。
/// </summary>
internal sealed class Unused :
    IReserveRepository,
    IRecordingStore,
    IEpgRepository,
    IMirakurunClient,
    IThumbnailService,
    IVideoFileRepository,
    IMediaProbe,
    IEncodeExecutor,
    IRecordedHistoryStore,
    ICommandHookRunner,
    IRecordedItemRepository,
    IDropLogRepository
{
    public static readonly Unused Instance = new();

    private static InvalidOperationException NotExpected() =>
        new("この依存は呼ばれない前提の試験です。");

    public ValueTask<Page<Reservation>> ListAsync(ReserveQuery query, CancellationToken cancellationToken) =>
        throw NotExpected();

    public ValueTask<long> AddAsync(CreateReserveCommand command, CancellationToken cancellationToken) =>
        throw NotExpected();

    public ValueTask<Reservation?> GetAsync(long reserveId, CancellationToken cancellationToken) =>
        throw NotExpected();

    ValueTask<bool> IReserveRepository.DeleteAsync(long reserveId, CancellationToken cancellationToken) =>
        throw NotExpected();

    public ValueTask<bool> SetSkipAsync(long reserveId, bool isSkip, CancellationToken cancellationToken) =>
        throw NotExpected();

    public ValueTask<bool> ClearOverlapAsync(long reserveId, CancellationToken cancellationToken) =>
        throw NotExpected();

    public ValueTask<bool> UpdateAsync(long reserveId, CreateReserveCommand command, CancellationToken cancellationToken) =>
        throw NotExpected();

    public ValueTask<(long RecordedId, long VideoFileId)> BeginAsync(
        RecordingStart start,
        string parentDirectoryName,
        string filename,
        CancellationToken cancellationToken) =>
        throw NotExpected();

    public ValueTask CompleteAsync(long recordedId, long videoFileId, long size, CancellationToken cancellationToken) =>
        throw NotExpected();

    public ValueTask SaveDropLogAsync(
        long recordedId,
        TransportStreamDefects defects,
        string parentDirectoryName,
        string filename,
        CancellationToken cancellationToken) =>
        throw NotExpected();

    public ValueTask AbortAsync(long recordedId, CancellationToken cancellationToken) =>
        throw NotExpected();

    public ValueTask<bool> ExistsAsync(long programId, CancellationToken cancellationToken) =>
        throw NotExpected();

    public ValueTask<IReadOnlyList<UnfinishedRecording>> ListUnfinishedAsync(CancellationToken cancellationToken) =>
        throw NotExpected();

    public ValueTask<IReadOnlyList<EpgChannel>> ListChannelsAsync(CancellationToken cancellationToken) =>
        throw NotExpected();

    public ValueTask<EpgChannel?> GetChannelAsync(long channelId, CancellationToken cancellationToken) =>
        throw NotExpected();

    public ValueTask<EpgProgram?> GetProgramAsync(long programId, CancellationToken cancellationToken) =>
        throw NotExpected();

    public ValueTask<IReadOnlyList<EpgProgram>> FindProgramsByIdsAsync(
        IReadOnlyList<long> programIds,
        CancellationToken cancellationToken) =>
        throw NotExpected();

    public ValueTask<IReadOnlyList<EpgProgram>> FindProgramsAsync(EpgScheduleQuery query, CancellationToken cancellationToken) =>
        throw NotExpected();

    public ValueTask<IReadOnlyList<EpgProgram>> SearchProgramsAsync(EpgSearchQuery query, CancellationToken cancellationToken) =>
        throw NotExpected();

    public ValueTask<IReadOnlyList<MirakurunServiceDto>> GetServicesAsync(CancellationToken cancellationToken) =>
        throw NotExpected();

    public ValueTask<IReadOnlyList<MirakurunProgramDto>> GetProgramsAsync(CancellationToken cancellationToken) =>
        throw NotExpected();

    public IAsyncEnumerable<MirakurunEventDto> ReadEventsAsync(CancellationToken cancellationToken) =>
        throw NotExpected();

    public ValueTask<IReadOnlyList<MirakurunTunerDto>> GetTunersAsync(CancellationToken cancellationToken) =>
        throw NotExpected();

    public ValueTask<Stream> OpenServiceStreamAsync(long channelId, CancellationToken cancellationToken, int? priority = null) =>
        throw NotExpected();

    ValueTask<ThumbnailFile?> IThumbnailService.GetAsync(long thumbnailId, CancellationToken cancellationToken) =>
        throw NotExpected();

    public ValueTask<long?> CreateForVideoFileAsync(long videoFileId, CancellationToken cancellationToken) =>
        throw NotExpected();

    public ValueTask<int> CreateMissingAsync(CancellationToken cancellationToken) =>
        throw NotExpected();

    ValueTask<bool> IThumbnailService.DeleteAsync(long thumbnailId, CancellationToken cancellationToken) =>
        throw NotExpected();

    public ValueTask<int> CleanupAsync(CancellationToken cancellationToken) =>
        throw NotExpected();

    ValueTask<RecordedCleanupResult> IRecordedItemRepository.CleanupAsync(CancellationToken cancellationToken) =>
        throw NotExpected();

    ValueTask<VideoFileLocation?> IVideoFileRepository.GetAsync(long videoFileId, CancellationToken cancellationToken) =>
        throw NotExpected();

    ValueTask<bool> IVideoFileRepository.DeleteAsync(long videoFileId, CancellationToken cancellationToken) =>
        throw NotExpected();

    public ValueTask<double?> GetDurationSecondsAsync(string path, CancellationToken cancellationToken) =>
        throw NotExpected();

    public Task<bool> RunAsync(
        string inputPath,
        string outputPath,
        IReadOnlyList<string> arguments,
        string? command,
        double? rateTimeoutMultiplier,
        IReadOnlyDictionary<string, string> environmentVariables,
        Func<int?, string?, CancellationToken, Task> onProgress,
        CancellationToken cancellationToken) =>
        throw NotExpected();

    public ValueTask AddAsync(string name, long channelId, DateTimeOffset endAt, CancellationToken cancellationToken) =>
        throw NotExpected();

    public ValueTask<IReadOnlyList<RecordedHistoryItem>> ListAsync(CancellationToken cancellationToken) =>
        throw NotExpected();

    public ValueTask<int> PurgeAsync(DateTimeOffset threshold, CancellationToken cancellationToken) =>
        throw NotExpected();

    public void RunReserveHook(string? command, ReserveHookPayload payload) => throw NotExpected();

    public void RunRecordedHook(string? command, RecordedHookPayload payload) => throw NotExpected();

    public void RunEncodeFinishHook(string? command, EncodeFinishHookPayload payload) => throw NotExpected();

    ValueTask<RecordedProgram?> IRecordedItemRepository.GetAsync(long recordedId, CancellationToken cancellationToken) =>
        throw NotExpected();

    ValueTask<bool> IRecordedItemRepository.DeleteAsync(long recordedId, CancellationToken cancellationToken) =>
        throw NotExpected();

    public ValueTask<bool> SetProtectedAsync(long recordedId, bool isProtected, CancellationToken cancellationToken) =>
        throw NotExpected();

    ValueTask<DropLogFileLocation?> IDropLogRepository.GetAsync(long dropLogFileId, CancellationToken cancellationToken) =>
        throw NotExpected();
}
