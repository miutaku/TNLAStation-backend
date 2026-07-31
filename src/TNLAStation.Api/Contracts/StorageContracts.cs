using TNLAStation.Domain;

namespace TNLAStation.Api.Contracts;

public sealed record StorageInfoResponse(IReadOnlyList<StorageItemResponse> Items);

/// <summary>
/// Property order follows EPGStation, which builds the disk usage object first and attaches the
/// name afterwards. <see cref="FileTypes"/> is a TNLAStation addition; it goes last so the
/// upstream keys keep their order.
/// </summary>
public sealed record StorageItemResponse(
    long Available,
    long Used,
    long Total,
    string Name,
    IReadOnlyList<StorageFileUsageResponse> FileTypes);

/// <summary>
/// Aggregate size and count for one kind of file below a recording destination. TNLAStation only.
/// </summary>
public sealed record StorageFileUsageResponse(
    string Category,
    string Format,
    long Count,
    long Size);

internal static class StorageContractMapper
{
    public static StorageInfoResponse ToResponse(this IEnumerable<StorageUsage> items) =>
        new(items.Select(item => new StorageItemResponse(
            item.Available,
            item.Used,
            item.Total,
            item.Name,
            item.FileTypes.Select(fileType => new StorageFileUsageResponse(
                fileType.Category,
                fileType.Format,
                fileType.Count,
                fileType.Size)).ToArray())).ToArray());
}
