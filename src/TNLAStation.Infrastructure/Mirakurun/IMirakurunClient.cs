namespace TNLAStation.Infrastructure.Mirakurun;

/// <summary>
/// Mirakurun から番組表を取る口。EPG 同期の異常系を、実際のチューナーなしで確かめられるように
/// 実装から切り離す。
/// </summary>
public interface IMirakurunClient
{
    ValueTask<IReadOnlyList<MirakurunServiceDto>> GetServicesAsync(CancellationToken cancellationToken);

    ValueTask<IReadOnlyList<MirakurunProgramDto>> GetProgramsAsync(CancellationToken cancellationToken);

    IAsyncEnumerable<MirakurunEventDto> ReadEventsAsync(CancellationToken cancellationToken);

    /// <summary>
    /// 放送中の MPEG-TS を開く。呼び出し側が閉じるまでチューナーを占有し続けるので、
    /// 返ってきた <see cref="Stream"/> は必ず破棄する。
    /// </summary>
    ValueTask<Stream> OpenServiceStreamAsync(long channelId, CancellationToken cancellationToken);
}
