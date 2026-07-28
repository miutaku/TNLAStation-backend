using Microsoft.AspNetCore.Mvc;
using TNLAStation.Api.Contracts;
using TNLAStation.Application.Abstractions;

namespace TNLAStation.Api.Endpoints;

/// <summary>
/// 受信の取りこぼしの記録。録画が信用できるかどうかは、再生する前に知りたい。
/// </summary>
internal static class DropLogEndpoints
{
    public static IEndpointRouteBuilder MapDropLogEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/dropLogs/{dropLogFileId}", GetDropLogAsync)
            .WithName("GetDropLog")
            .WithSummary("ドロップログ")
            .WithTags("dropLogs");

        return endpoints;
    }

    private static async Task<IResult> GetDropLogAsync(
        long dropLogFileId,
        IDropLogRepository repository,
        [FromQuery] int maxsize = 512,
        CancellationToken cancellationToken = default)
    {
        DropLogFileLocation? log = await repository.GetAsync(dropLogFileId, cancellationToken);
        if (log is null || !File.Exists(log.FullPath))
        {
            return Results.Json(
                new ErrorResponse(StatusCodes.Status404NotFound, "drop log file is not Found"),
                statusCode: StatusCodes.Status404NotFound);
        }

        var file = new FileInfo(log.FullPath);
        if (maxsize > 0 && file.Length > (long)maxsize * 1024)
        {
            // 読み切れない大きさのものを黙って途中まで返すと、途中で終わったことが
            // 読む側から見て分からない。
            return Results.StatusCode(StatusCodes.Status416RangeNotSatisfiable);
        }

        return Results.File(File.OpenRead(log.FullPath), "text/plain");
    }
}
