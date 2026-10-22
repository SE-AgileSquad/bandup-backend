namespace BandUp.Application.Common;

public sealed record Error(string Code, string Message, ErrorType Type)
{
    public const string ValidationCode = "VALIDATION_ERROR";
    public const string NotFoundCode = "NOT_FOUND";
    public const string ConflictCode = "CONFLICT";
    public const string ForbiddenCode = "FORBIDDEN";
    public const string UnauthorizedCode = "UNAUTHORIZED";
    public const string InternalErrorCode = "INTERNAL_ERROR";

    public IReadOnlyDictionary<string, string[]> ValidationErrors { get; init; } =
        new Dictionary<string, string[]>(StringComparer.Ordinal);

    public static Error Validation(IReadOnlyDictionary<string, string[]> errors) =>
        new(ValidationCode, "One or more validation errors occurred.", ErrorType.Validation)
        {
            ValidationErrors = errors,
        };

    public static Error NotFound(string message) => new(NotFoundCode, message, ErrorType.NotFound);

    public static Error Conflict(string message) => new(ConflictCode, message, ErrorType.Conflict);

    public static Error Forbidden(string message) => new(ForbiddenCode, message, ErrorType.Forbidden);

    public static Error Unauthorized(string message) => new(UnauthorizedCode, message, ErrorType.Unauthorized);
}