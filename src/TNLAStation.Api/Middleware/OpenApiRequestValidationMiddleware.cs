using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Primitives;
using Microsoft.AspNetCore.Routing;
using TNLAStation.Api.Contracts;

namespace TNLAStation.Api.Middleware;

/// <summary>
/// EPGStation v2.10.0 の OpenAPI schema に従って JSON body の必須プロパティを検証する。
/// System.Text.Json は欠落した非 nullable 値も既定値で構築できるため、これを挟まないと
/// 上流が 400 にする入力が変更処理まで到達してしまう。
/// </summary>
public sealed class OpenApiRequestValidationMiddleware(RequestDelegate next)
{
    private const string ResourceName =
        "TNLAStation.Api.Compatibility.epgstation-api-v2.10.0.json";

    private static readonly JsonDocument OpenApi = ReadOpenApi();

    public async Task InvokeAsync(HttpContext context)
    {
        OpenApiValidationError[] queryErrors = ValidateQuery(context);
        if (queryErrors.Length > 0)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsJsonAsync(
                new OpenApiValidationErrorResponse(StatusCodes.Status400BadRequest, queryErrors),
                context.RequestAborted);
            return;
        }

        JsonElement? schema = FindRequestSchema(context);
        if (schema is null)
        {
            await next(context);
            return;
        }

        if (context.Request.ContentType?.StartsWith("application/json", StringComparison.OrdinalIgnoreCase) != true)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsJsonAsync(
                new OpenApiValidationErrorResponse(
                    StatusCodes.Status415UnsupportedMediaType,
                    [new OpenApiValidationError(
                        $"Unsupported Content-Type {context.Request.ContentType ?? "undefined"}")]),
                context.RequestAborted);
            return;
        }

        context.Request.EnableBuffering();
        JsonDocument? body = null;
        string rawBody;
        using (var reader = new StreamReader(
                   context.Request.Body,
                   encoding: System.Text.Encoding.UTF8,
                   detectEncodingFromByteOrderMarks: true,
                   leaveOpen: true))
        {
            rawBody = await reader.ReadToEndAsync(context.RequestAborted);
            context.Request.Body.Position = 0;
        }

        try
        {
            body = JsonDocument.Parse(string.IsNullOrWhiteSpace(rawBody) ? "{}" : rawBody);
        }
        catch (JsonException)
        {
            await WriteEntityParseFailedAsync(context, rawBody);
            return;
        }

        using (body)
        {
            // body-parser の strict=true は object/array 以外の JSON 値を parse error にする。
            if (body.RootElement.ValueKind is not (JsonValueKind.Object or JsonValueKind.Array))
            {
                await WriteEntityParseFailedAsync(context, rawBody);
                return;
            }

            List<OpenApiValidationError> errors = [];
            CollectSchemaErrors(Resolve(schema.Value), body.RootElement, errors);

            if (errors.Count == 0)
            {
                await next(context);
                return;
            }

            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsJsonAsync(
                new OpenApiValidationErrorResponse(StatusCodes.Status400BadRequest, errors),
                context.RequestAborted);
        }
    }

    private static async Task WriteEntityParseFailedAsync(HttpContext context, string body)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        await context.Response.WriteAsJsonAsync(
            new
            {
                expose = true,
                statusCode = StatusCodes.Status400BadRequest,
                status = StatusCodes.Status400BadRequest,
                body,
                type = "entity.parse.failed",
            },
            context.RequestAborted);
    }

    private static void CollectSchemaErrors(
        JsonElement schema,
        JsonElement value,
        List<OpenApiValidationError> errors)
    {
        schema = Resolve(schema);
        if (schema.TryGetProperty("nullable", out JsonElement nullable) &&
            nullable.GetBoolean() &&
            value.ValueKind == JsonValueKind.Null)
        {
            return;
        }

        if (schema.TryGetProperty("allOf", out JsonElement allOf))
        {
            foreach (JsonElement member in allOf.EnumerateArray())
            {
                CollectSchemaErrors(member, value, errors);
            }
        }

        if (schema.TryGetProperty("type", out JsonElement typeElement))
        {
            string type = typeElement.GetString()!;
            bool validType = type switch
            {
                "object" => value.ValueKind == JsonValueKind.Object,
                "array" => value.ValueKind == JsonValueKind.Array,
                "string" => value.ValueKind == JsonValueKind.String,
                "boolean" => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
                "integer" => value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out _),
                "number" => value.ValueKind == JsonValueKind.Number,
                _ => true,
            };
            if (!validType)
            {
                errors.Add(new OpenApiValidationError($"must be {type}"));
                return;
            }
        }

        if (value.ValueKind == JsonValueKind.Number &&
            schema.TryGetProperty("minimum", out JsonElement minimum) &&
            value.GetDouble() < minimum.GetDouble())
        {
            errors.Add(new OpenApiValidationError($"must be >= {minimum.GetRawText()}"));
        }

        if (schema.TryGetProperty("enum", out JsonElement allowed) &&
            !allowed.EnumerateArray().Any(item => item.GetRawText() == value.GetRawText()))
        {
            errors.Add(new OpenApiValidationError("must be equal to one of the allowed values"));
        }

        if (value.ValueKind == JsonValueKind.Object &&
            schema.TryGetProperty("required", out JsonElement required))
        {
            foreach (JsonElement item in required.EnumerateArray())
            {
                string name = item.GetString()!;
                if (!value.TryGetProperty(name, out _))
                {
                    errors.Add(new OpenApiValidationError($"must have required property '{name}'"));
                }
            }
        }

        if (value.ValueKind == JsonValueKind.Array &&
            schema.TryGetProperty("items", out JsonElement items))
        {
            foreach (JsonElement item in value.EnumerateArray())
            {
                CollectSchemaErrors(items, item, errors);
            }
        }

        if (value.ValueKind != JsonValueKind.Object ||
            !schema.TryGetProperty("properties", out JsonElement properties))
        {
            return;
        }

        foreach (JsonProperty property in properties.EnumerateObject())
        {
            if (value.TryGetProperty(property.Name, out JsonElement propertyValue))
            {
                CollectSchemaErrors(property.Value, propertyValue, errors);
            }
        }
    }

    private static JsonElement? FindRequestSchema(HttpContext context)
    {
        JsonElement? operation = FindOperation(context);
        if (operation is null ||
            !operation.Value.TryGetProperty("requestBody", out JsonElement requestBody))
        {
            return null;
        }

        requestBody = Resolve(requestBody);
        JsonElement content = requestBody.GetProperty("content");
        return content.TryGetProperty("application/json", out JsonElement json)
            ? json.GetProperty("schema")
            : null;
    }

    private static OpenApiValidationError[] ValidateQuery(HttpContext context)
    {
        JsonElement? operation = FindOperation(context);
        if (operation is null ||
            !operation.Value.TryGetProperty("parameters", out JsonElement parameters))
        {
            return [];
        }

        List<OpenApiValidationError> errors = [];
        List<(JsonElement Parameter, string Name)> queryParameters = [];
        foreach (JsonElement parameterReference in parameters.EnumerateArray())
        {
            JsonElement parameter = Resolve(parameterReference);
            string location = parameter.GetProperty("in").GetString()!;
            string name = parameter.GetProperty("name").GetString()!;
            if (location == "path")
            {
                JsonElement schema = Resolve(parameter.GetProperty("schema"));
                string? value = context.Request.RouteValues[name]?.ToString();
                if (schema.TryGetProperty("type", out JsonElement pathType) &&
                    pathType.GetString() == "integer" &&
                    !long.TryParse(value, out _))
                {
                    errors.Add(new OpenApiValidationError("must be integer"));
                }

                continue;
            }

            if (location != "query")
            {
                continue;
            }

            queryParameters.Add((parameter, name));
            if (parameter.TryGetProperty("required", out JsonElement required) &&
                required.GetBoolean() &&
                !context.Request.Query.ContainsKey(name))
            {
                errors.Add(new OpenApiValidationError($"must have required property '{name}'"));
            }
        }

        Dictionary<string, StringValues>? normalizedQuery = null;
        foreach ((JsonElement parameter, string name) in queryParameters)
        {
            if (!context.Request.Query.TryGetValue(name, out StringValues values) ||
                !parameter.TryGetProperty("schema", out JsonElement schema))
            {
                continue;
            }

            schema = Resolve(schema);

            // express-openapi の coercion は boolean 文字列を真偽値へ変換する。
            if (schema.TryGetProperty("type", out JsonElement type) &&
                type.GetString() == "integer")
            {
                foreach (string? value in values)
                {
                    if (!long.TryParse(value, out long number))
                    {
                        errors.Add(new OpenApiValidationError("must be integer"));
                    }
                    else if (schema.TryGetProperty("minimum", out JsonElement minimum) &&
                             number < minimum.GetInt64())
                    {
                        errors.Add(new OpenApiValidationError($"must be >= {minimum.GetRawText()}"));
                    }
                }
            }
            else if (schema.TryGetProperty("type", out type) &&
                     type.GetString() == "boolean" &&
                     values.Any(value =>
                         !string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) &&
                         !string.Equals(value, "false", StringComparison.OrdinalIgnoreCase)))
            {
                // AJV の coerceTypes は空でない未知文字列も true に変換する。
                // ASP.NET Core の bool binder が先に 400 にしないよう同じ値へ正規化する。
                normalizedQuery ??= context.Request.Query.ToDictionary(
                    pair => pair.Key,
                    pair => pair.Value,
                    StringComparer.OrdinalIgnoreCase);
                normalizedQuery[name] = new StringValues(values
                    .Select(value =>
                        string.Equals(value, "false", StringComparison.OrdinalIgnoreCase) ? "false" : "true")
                    .ToArray());
            }

            if (schema.TryGetProperty("enum", out JsonElement allowed) &&
                values.Any(value => !allowed.EnumerateArray().Any(item => item.GetString() == value)))
            {
                errors.Add(new OpenApiValidationError("must be equal to one of the allowed values"));
            }
        }

        if (normalizedQuery is not null)
        {
            context.Request.Query = new QueryCollection(normalizedQuery);
        }

        return [.. errors];
    }

    private static JsonElement? FindOperation(HttpContext context)
    {
        if (context.GetEndpoint() is not RouteEndpoint endpoint)
        {
            return null;
        }

        string path = "/" + (endpoint.RoutePattern.RawText ?? string.Empty)
            .Trim('/')
            .TrimStart('/');
        path = Regex.Replace(path, @"\{([^}:]+):[^}]+\}", "{$1}");
        if (path.StartsWith("/api/", StringComparison.Ordinal))
        {
            path = path["/api".Length..];
        }

        JsonElement root = OpenApi.RootElement;
        if (!root.GetProperty("paths").TryGetProperty(path, out JsonElement pathItem) ||
            !pathItem.TryGetProperty(context.Request.Method.ToLowerInvariant(), out JsonElement operation))
        {
            return null;
        }

        return operation;
    }

    private static JsonElement Resolve(JsonElement element)
    {
        while (element.ValueKind == JsonValueKind.Object &&
               element.TryGetProperty("$ref", out JsonElement reference))
        {
            element = reference.GetString()!["#/".Length..]
                .Split('/')
                .Aggregate(OpenApi.RootElement, (current, segment) => current.GetProperty(segment));
        }

        return element;
    }

    private static JsonDocument ReadOpenApi()
    {
        using Stream stream = typeof(OpenApiRequestValidationMiddleware).Assembly
            .GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Embedded resource {ResourceName} was not found.");
        return JsonDocument.Parse(stream);
    }
}
