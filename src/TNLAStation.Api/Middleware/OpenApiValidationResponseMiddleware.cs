using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using TNLAStation.Api.Contracts;

namespace TNLAStation.Api.Middleware;

/// <summary>
/// Minimal API の必須 query binding が返す本文なしの 400 を、express-openapi と同じ
/// status/errors JSON に変換する。
/// </summary>
public sealed class OpenApiValidationResponseMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        await next(context);

        if (context.Response.StatusCode != StatusCodes.Status400BadRequest ||
            context.Response.HasStarted ||
            context.Response.ContentLength is > 0 ||
            !string.IsNullOrEmpty(context.Response.ContentType))
        {
            return;
        }

        OpenApiValidationErrorResponse? response = CreateMissingQueryResponse(context);
        if (response is null)
        {
            return;
        }

        context.Response.ContentType = "application/json; charset=utf-8";
        await context.Response.WriteAsJsonAsync(response, context.RequestAborted);
    }

    internal static OpenApiValidationErrorResponse? CreateMissingQueryResponse(HttpContext context)
    {
        MethodInfo? method = context.GetEndpoint()?.Metadata.GetMetadata<MethodInfo>();
        if (method is null)
        {
            return null;
        }

        OpenApiValidationError[] errors =
        [
            .. method.GetParameters()
                .Select(parameter => (Parameter: parameter, Query: parameter.GetCustomAttribute<FromQueryAttribute>()))
                .Where(item =>
                    item.Query is not null &&
                    !item.Parameter.HasDefaultValue &&
                    Nullable.GetUnderlyingType(item.Parameter.ParameterType) is null &&
                    !context.Request.Query.ContainsKey(item.Query.Name ?? item.Parameter.Name!))
                .Select(item => new OpenApiValidationError(
                    $"must have required property '{item.Query!.Name ?? item.Parameter.Name}'")),
        ];

        if (errors.Length == 0)
        {
            return null;
        }

        return new OpenApiValidationErrorResponse(StatusCodes.Status400BadRequest, errors);
    }
}
