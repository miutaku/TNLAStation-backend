using TNLAStation.Api.Contracts;

namespace TNLAStation.Api.Middleware;

internal sealed partial class EpgStationExceptionMiddleware(
    RequestDelegate next,
    ILogger<EpgStationExceptionMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (Exception exception) when (!context.Response.HasStarted)
        {
            context.Response.Clear();

            if (exception is BadHttpRequestException badRequest)
            {
                context.Response.StatusCode = badRequest.StatusCode;
                OpenApiValidationErrorResponse? missingQuery =
                    OpenApiValidationResponseMiddleware.CreateMissingQueryResponse(context);
                object response = missingQuery ?? (object)new ValidationErrorResponse(badRequest.Message);
                await context.Response.WriteAsJsonAsync(
                    response,
                    context.RequestAborted);
                return;
            }

            LogUnhandledException(logger, exception);
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            await context.Response.WriteAsJsonAsync(
                new ErrorResponse(
                    StatusCodes.Status500InternalServerError,
                    "Internal Server Error",
                    exception.Message),
                context.RequestAborted);
        }
    }

    [LoggerMessage(
        EventId = 1000,
        Level = LogLevel.Error,
        Message = "Unhandled exception while processing an EPGStation-compatible request")]
    private static partial void LogUnhandledException(ILogger logger, Exception exception);
}
