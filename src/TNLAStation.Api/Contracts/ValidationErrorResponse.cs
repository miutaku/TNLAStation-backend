namespace TNLAStation.Api.Contracts;

public sealed record ValidationErrorResponse(string Message);

public sealed record OpenApiValidationErrorResponse(int Status, IReadOnlyList<OpenApiValidationError> Errors);

public sealed record OpenApiValidationError(string Message);
