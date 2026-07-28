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
    /// チューナーの一覧。何本あって何を受信できるかで、同時に録れる番組が決まる。
    /// </summary>
    ValueTask<IReadOnlyList<MirakurunTunerDto>> GetTunersAsync(CancellationToken cancellationToken);

    /// <summary>
    /// 放送中の MPEG-TS を開く。呼び出し側が閉じるまでチューナーを占有し続けるので、
    /// 返ってきた <see cref="Stream"/> は必ず破棄する。
    /// </summary>
    /// <param name="priority">
    /// チューナーの取り合いになったときの優先度 (Mirakurun 仕様)。録画以外の視聴では
    /// 指定しない — EPGStation も録画時にしか使わない。
    /// </param>
    ValueTask<Stream> OpenServiceStreamAsync(long channelId, CancellationToken cancellationToken, int? priority = null);
}
