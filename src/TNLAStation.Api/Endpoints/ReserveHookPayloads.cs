using TNLAStation.Application.Abstractions;
using TNLAStation.Domain;

namespace TNLAStation.Api.Endpoints;

/// <summary>予約の追加・更新・削除フック向けに、予約と番組表から共通の payload を組み立てる。</summary>
internal static class ReserveHookPayloads
{
    public static async ValueTask<ReserveHookPayload> BuildAsync(
        Reservation reserve,
        IEpgRepository epg,
        CancellationToken cancellationToken)
    {
        EpgChannel? channel = await epg.GetChannelAsync(reserve.ChannelId, cancellationToken);
        return new ReserveHookPayload(
            reserve.Id,
            reserve.ProgramId,
            reserve.ChannelId,
            channel?.Name ?? "CH",
            channel?.HalfWidthName ?? "CH",
            reserve.StartAt,
            reserve.EndAt,
            reserve.Name,
            reserve.HalfWidthName,
            reserve.Description,
            reserve.HalfWidthDescription,
            reserve.Extended,
            reserve.HalfWidthExtended,
            channel?.ChannelType);
    }
}
