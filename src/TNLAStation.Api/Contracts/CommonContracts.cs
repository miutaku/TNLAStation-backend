namespace TNLAStation.Api.Contracts;

public sealed record ErrorResponse(int Code, string Message, string? Errors = null);

public sealed record VersionResponse(string Version);
