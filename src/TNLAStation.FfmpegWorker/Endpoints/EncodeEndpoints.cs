using System.Text.Json;
using TNLAStation.FfmpegWorker.Contracts;
using TNLAStation.FfmpegWorker.Media;

namespace TNLAStation.FfmpegWorker.Endpoints;

public static class EncodeEndpoints
{
    public static void MapEncodeEndpoints(this WebApplication app)
    {
        app.MapPost("/encode", async (EncodeRequest request, EncodeRunner runner, HttpContext context) =>
        {
            context.Response.ContentType = "application/x-ndjson";
            bool succeeded = false;
            bool preempted = false;
            Exception? failure = null;
            try
            {
                succeeded = await runner.RunAsync(
                    request.InputPath,
                    request.OutputPath,
                    request.Arguments,
                    request.Command,
                    request.RateTimeoutMultiplier,
                    request.EnvironmentVariables ?? new Dictionary<string, string>(),
                    async (percent, log, cancellationToken) =>
                        await WriteLineAsync(context, new EncodeProgress(Done: false, Succeeded: false, percent, log), cancellationToken),
                    context.RequestAborted);
            }
            catch (TNLAStation.Application.Abstractions.EncodePreemptedException)
            {
                preempted = true;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                failure = exception;
            }

            await WriteLineAsync(
                context,
                new EncodeProgress(Done: true, Succeeded: succeeded, Percent: null, Log: failure?.Message, Preempted: preempted),
                CancellationToken.None);
        });
    }

    private static async Task WriteLineAsync(HttpContext context, EncodeProgress progress, CancellationToken cancellationToken)
    {
        await context.Response.WriteAsync(JsonSerializer.Serialize(progress), cancellationToken);
        await context.Response.WriteAsync("\n", cancellationToken);
        await context.Response.Body.FlushAsync(cancellationToken);
    }
}
