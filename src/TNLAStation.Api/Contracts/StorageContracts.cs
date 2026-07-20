using TNLAStation.Domain;

namespace TNLAStation.Api.Contracts;

public sealed record StorageInfoResponse(IReadOnlyList<StorageItemResponse> Items);

/// <summary>
/// Property order follows EPGStation, which builds the disk usage object first and attaches the
/// name afterwards.
/// </summary>
public sealed record StorageItemResponse(long Available, long Used, long Total, string Name);

internal static class StorageContractMapper
{
    public static StorageInfoResponse ToResponse(this IEnumerable<StorageUsage> items) =>
        new(items.Select(item => new StorageItemResponse(
            item.Available,
            item.Used,
            item.Total,
            item.Name)).ToArray());
}
