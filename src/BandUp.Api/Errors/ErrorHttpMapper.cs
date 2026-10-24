using BandUp.Application.Common;
using Microsoft.AspNetCore.WebUtilities;

namespace BandUp.Api.Errors;

/// <summary>
/// The single place that turns an <see cref="Error"/> into an HTTP status and a problem type.
/// Endpoints and the exception handler both go through here, so the mapping cannot diverge.
/// </summary>
public static class ErrorHttpMapper
{
    public const string ErrorTypeUriPrefix = "https://bandup.app/errors/";

    public static int StatusFor(ErrorType type) => type switch
    {
        ErrorType.Validation => StatusCodes.Status400BadRequest,
        ErrorType.Unauthorized => StatusCodes.Status401Unauthorized,
        ErrorType.PaymentRequired => StatusCodes.Status402PaymentRequired,
        ErrorType.Forbidden => StatusCodes.Status403Forbidden,
        ErrorType.NotFound => StatusCodes.Status404NotFound,
        ErrorType.Conflict => StatusCodes.Status409Conflict,
        ErrorType.Unprocessable => StatusCodes.Status422UnprocessableEntity,
        ErrorType.TooManyRequests => StatusCodes.Status429TooManyRequests,
        ErrorType.Unavailable => StatusCodes.Status502BadGateway,
        _ => StatusCodes.Status500InternalServerError,
    };

    /// <summary>
    /// Error codes are uppercase constants joined by underscores, so <c>VALIDATION_ERROR</c>
    /// becomes <c>validation-error</c>.
    /// </summary>
    public static string TypeUriFor(string code) =>
        ErrorTypeUriPrefix + code.Replace('_', '-').ToLowerInvariant();

    public static string TitleFor(int statusCode) => ReasonPhrases.GetReasonPhrase(statusCode);
}