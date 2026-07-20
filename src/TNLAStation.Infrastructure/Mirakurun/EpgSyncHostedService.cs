using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TNLAStation.Application.Abstractions;
using TNLAStation.Domain;
using TNLAStation.Infrastructure.Configuration;

namespace TNLAStation.Infrastructure.Mirakurun;

public sealed partial class EpgSyncHostedService(
    IMirakurunClient client,
    MirakurunEpgMapper mapper,
    IEpgStore store,
    IEpgSyncLeaseProvider leaseProvider,
    IOptions<MirakurunOptions> mirakurunOptions,
    IOptions<EpgOptions> epgOptions,
    TimeProvider timeProvider,
    ILogger<EpgSyncHostedService> logger) : BackgroundService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = false
    };
    private readonly MirakurunOptions mirakurunOptions = mirakurunOptions.Value;
    private readonly EpgOptions epgOptions = epgOptions.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await using IAsyncDisposable? lease = await leaseProvider.TryAcquireAsync(stoppingToken);
            if (lease is null)
            {
                LogLeaseUnavailable(logger);
                await Task.Delay(TimeSpan.FromSeconds(30), timeProvider, stoppingToken);
                continue;
            }

            LogLeaseAcquired(logger);
            await RunLeaderLoopAsync(stoppingToken);
        }
    }

    private async Task RunLeaderLoopAsync(CancellationToken cancellationToken)
    {
        int retryCount = 0;
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                retryCount = 0;
                await RunEventSessionAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                DateTimeOffset attemptedAt = timeProvider.GetUtcNow();
                LogSyncFailed(logger, exception);
                try
                {
                    await store.RecordSyncFailureAsync(attemptedAt, exception.Message, cancellationToken);
                }
                catch (Exception stateException)
                {
                    LogSyncFailureStateNotPersisted(logger, stateException);
                }

                retryCount = Math.Min(retryCount + 1, 6);
                double maximumSeconds = Math.Min(60, Math.Pow(2, retryCount));
                TimeSpan delay = TimeSpan.FromMilliseconds(Random.Shared.NextDouble() * maximumSeconds * 1000);
                await Task.Delay(delay, timeProvider, cancellationToken);
            }
        }
    }

    private async Task RunEventSessionAsync(CancellationToken cancellationToken)
    {
        using var sessionCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var eventChannel = Channel.CreateBounded<MirakurunEventDto>(new BoundedChannelOptions(
            Math.Max(16, mirakurunOptions.EventQueueCapacity))
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = true
        });

        // Open the event stream before requesting the snapshot. Events emitted while the two
        // snapshot requests are in flight are buffered and applied after the atomic commit.
        Task producer = ProduceEventsAsync(eventChannel.Writer, sessionCancellation.Token);
        try
        {
            IReadOnlyDictionary<(int NetworkId, int ServiceId), EpgChannel> channelIndex =
                await ReplaceFullSnapshotAsync(cancellationToken);
            await ConsumeEventStreamAsync(
                channelIndex,
                eventChannel.Reader,
                producer,
                cancellationToken);
        }
        finally
        {
            await sessionCancellation.CancelAsync();
            await producer;
        }
    }

    private async Task<IReadOnlyDictionary<(int NetworkId, int ServiceId), EpgChannel>> ReplaceFullSnapshotAsync(
        CancellationToken cancellationToken)
    {
        LogSnapshotFetching(logger);
        Task<IReadOnlyList<MirakurunServiceDto>> servicesTask = client.GetServicesAsync(cancellationToken).AsTask();
        Task<IReadOnlyList<MirakurunProgramDto>> programsTask = client.GetProgramsAsync(cancellationToken).AsTask();
        await Task.WhenAll(servicesTask, programsTask);

        DateTimeOffset capturedAt = timeProvider.GetUtcNow();
        IReadOnlyList<EpgChannel> channels = mapper.MapChannels(await servicesTask);
        IReadOnlyDictionary<(int NetworkId, int ServiceId), EpgChannel> channelIndex =
            MirakurunEpgMapper.CreateChannelIndex(channels);
        IReadOnlyList<EpgProgram> programs = mapper.MapPrograms(await programsTask, channelIndex, capturedAt);
        await store.ReplaceSnapshotAsync(new EpgSnapshot(channels, programs, capturedAt), cancellationToken);
        await store.DeleteProgramsEndingBeforeAsync(capturedAt, cancellationToken);
        LogSnapshotCommitted(logger, channels.Count, programs.Count);
        return channelIndex;
    }

    private async Task ConsumeEventStreamAsync(
        IReadOnlyDictionary<(int NetworkId, int ServiceId), EpgChannel> initialChannelIndex,
        ChannelReader<MirakurunEventDto> eventReader,
        Task producer,
        CancellationToken cancellationToken)
    {
        var state = new EventConsumptionState(initialChannelIndex, timeProvider.GetUtcNow());
        DateTimeOffset nextFullSync = timeProvider.GetUtcNow().Add(GetUpdateInterval());

        DrainEvents(eventReader, state);
        await ApplyPendingChangesAsync(state, timeProvider.GetUtcNow(), cancellationToken);
        await ThrowIfProducerCompletedAsync(eventReader, producer);

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(10), timeProvider);
        while (await timer.WaitForNextTickAsync(cancellationToken))
        {
            DateTimeOffset now = timeProvider.GetUtcNow();
            if (now >= nextFullSync)
            {
                // Flush everything observed before the full snapshot first. The stream stays open
                // during replacement, then the second drain applies events emitted in that window.
                DrainEvents(eventReader, state);
                await ApplyPendingChangesAsync(state, now, cancellationToken);
                state.ChannelIndex = await ReplaceFullSnapshotAsync(cancellationToken);
                nextFullSync = now.Add(GetUpdateInterval());
            }

            DrainEvents(eventReader, state);
            await ApplyPendingChangesAsync(state, now, cancellationToken);
            await ThrowIfProducerCompletedAsync(eventReader, producer);
        }
    }

    private void DrainEvents(ChannelReader<MirakurunEventDto> eventReader, EventConsumptionState state)
    {
        while (eventReader.TryRead(out MirakurunEventDto? item))
        {
            state.LastEventAt = SafeEventTime(item.Time);
            switch (item.Resource)
            {
                case "service":
                    state.ServicesChanged = true;
                    break;
                case "program":
                    AddPendingProgramChange(state.PendingPrograms, item);
                    break;
            }
        }
    }

    private async Task ApplyPendingChangesAsync(
        EventConsumptionState state,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<EpgChannel> changedChannels = [];
        if (state.ServicesChanged)
        {
            IReadOnlyList<MirakurunServiceDto> serviceDtos = await client.GetServicesAsync(cancellationToken);
            changedChannels = mapper.MapChannels(serviceDtos);
            state.ChannelIndex = MirakurunEpgMapper.CreateChannelIndex(changedChannels);
        }

        if (changedChannels.Count == 0 && state.PendingPrograms.Count == 0)
        {
            state.ServicesChanged = false;
            return;
        }

        var upserts = new List<EpgProgram>();
        var deletes = new List<long>();
        foreach (PendingProgramChange change in state.PendingPrograms.Values)
        {
            if (change.DeleteId is not null)
            {
                deletes.Add(change.DeleteId.Value);
            }
            else if (change.Program is not null)
            {
                EpgProgram? program = mapper.MapProgram(change.Program, state.ChannelIndex, now);
                if (program is not null)
                {
                    upserts.Add(program);
                }
            }
        }

        await store.ApplyChangesAsync(
            changedChannels,
            upserts,
            deletes,
            state.LastEventAt,
            cancellationToken);
        state.PendingPrograms.Clear();
        state.ServicesChanged = false;
    }

    private static async Task ThrowIfProducerCompletedAsync(
        ChannelReader<MirakurunEventDto> eventReader,
        Task producer)
    {
        if (!producer.IsCompleted || !eventReader.Completion.IsCompleted)
        {
            return;
        }

        await producer;
        await eventReader.Completion;
        throw new EndOfStreamException("Mirakurun event producer completed unexpectedly.");
    }

    private async Task ProduceEventsAsync(
        ChannelWriter<MirakurunEventDto> writer,
        CancellationToken cancellationToken)
    {
        Exception? failure = null;
        try
        {
            await foreach (MirakurunEventDto item in client.ReadEventsAsync(cancellationToken))
            {
                await writer.WriteAsync(item, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The host is stopping.
        }
        catch (Exception exception)
        {
            failure = exception;
        }
        finally
        {
            writer.TryComplete(failure);
        }
    }

    private static void AddPendingProgramChange(
        IDictionary<long, PendingProgramChange> pendingPrograms,
        MirakurunEventDto item)
    {
        switch (item.Type)
        {
            case "create":
            case "update":
                MirakurunProgramDto? program = item.Data.Deserialize<MirakurunProgramDto>(JsonOptions);
                if (program is not null)
                {
                    pendingPrograms[program.Id] = new PendingProgramChange(program, null);
                }

                break;
            case "remove":
                MirakurunRemoveProgramDto? remove = item.Data.Deserialize<MirakurunRemoveProgramDto>(JsonOptions);
                if (remove is not null)
                {
                    pendingPrograms[remove.Id] = new PendingProgramChange(null, remove.Id);
                }

                break;
            case "redefine":
                MirakurunRedefineProgramDto? redefine = item.Data.Deserialize<MirakurunRedefineProgramDto>(JsonOptions);
                if (redefine is not null)
                {
                    pendingPrograms[redefine.From] = new PendingProgramChange(null, redefine.From);
                }

                break;
        }
    }

    [LoggerMessage(
        EventId = 2000,
        Level = LogLevel.Information,
        Message = "Another TNLAStation instance owns the EPG synchronization lease.")]
    private static partial void LogLeaseUnavailable(ILogger logger);

    [LoggerMessage(
        EventId = 2001,
        Level = LogLevel.Information,
        Message = "Acquired the EPG synchronization lease.")]
    private static partial void LogLeaseAcquired(ILogger logger);

    [LoggerMessage(
        EventId = 2002,
        Level = LogLevel.Error,
        Message = "EPG synchronization failed; the last committed snapshot remains active.")]
    private static partial void LogSyncFailed(ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = 2003,
        Level = LogLevel.Error,
        Message = "Failed to persist EPG synchronization failure state.")]
    private static partial void LogSyncFailureStateNotPersisted(ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = 2004,
        Level = LogLevel.Information,
        Message = "Fetching a full Mirakurun EPG snapshot.")]
    private static partial void LogSnapshotFetching(ILogger logger);

    [LoggerMessage(
        EventId = 2005,
        Level = LogLevel.Information,
        Message = "Committed Mirakurun EPG snapshot with {ChannelCount} channels and {ProgramCount} programs.")]
    private static partial void LogSnapshotCommitted(ILogger logger, int channelCount, int programCount);

    private TimeSpan GetUpdateInterval() =>
        TimeSpan.FromMinutes(Math.Max(1, epgOptions.UpdateIntervalMinutes));

    private DateTimeOffset SafeEventTime(long unixMilliseconds)
    {
        try
        {
            return DateTimeOffset.FromUnixTimeMilliseconds(unixMilliseconds);
        }
        catch (ArgumentOutOfRangeException)
        {
            return timeProvider.GetUtcNow();
        }
    }

    private sealed record PendingProgramChange(MirakurunProgramDto? Program, long? DeleteId);

    private sealed class EventConsumptionState(
        IReadOnlyDictionary<(int NetworkId, int ServiceId), EpgChannel> channelIndex,
        DateTimeOffset lastEventAt)
    {
        public IReadOnlyDictionary<(int NetworkId, int ServiceId), EpgChannel> ChannelIndex { get; set; } =
            channelIndex;

        public DateTimeOffset LastEventAt { get; set; } = lastEventAt;

        public bool ServicesChanged { get; set; }

        public Dictionary<long, PendingProgramChange> PendingPrograms { get; } = [];
    }
}
