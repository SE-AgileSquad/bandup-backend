namespace BandUp.Application.Common;

public sealed record Error(string Code, string Message, ErrorType Type)
{
    public const string ValidationCode = "VALIDATION_ERROR";
    public const string NotFoundCode = "NOT_FOUND";
    public const string ConflictCode = "CONFLICT";
    public const string ForbiddenCode = "FORBIDDEN";
    public const string UnauthorizedCode = "UNAUTHORIZED";
    public const string InternalErrorCode = "INTERNAL_ERROR";

    public const string EmailTakenCode = "EMAIL_TAKEN";
    public const string InvalidCredentialsCode = "INVALID_CREDENTIALS";
    public const string InvalidRefreshTokenCode = "INVALID_REFRESH_TOKEN";
    public const string InvalidPasswordResetCode = "INVALID_PASSWORD_RESET_CODE";
    public const string PasswordResetRateLimitedCode = "PASSWORD_RESET_RATE_LIMITED";
    public const string CaptchaFailedCode = "CAPTCHA_FAILED";
    public const string CaptchaUnavailableCode = "CAPTCHA_UNAVAILABLE";

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

    public static Error EmailTaken() =>
        new(EmailTakenCode, "An account with this email already exists.", ErrorType.Conflict);

    /// <summary>
    /// One message for both an unknown email and a wrong password, so the response cannot be
    /// used to discover which accounts exist.
    /// </summary>
    public static Error InvalidCredentials() =>
        new(InvalidCredentialsCode, "The email or password is incorrect.", ErrorType.Unauthorized);

    public static Error InvalidRefreshToken() =>
        new(InvalidRefreshTokenCode, "The session has expired. Sign in again.", ErrorType.Unauthorized);

    public static Error InvalidPasswordReset() =>
        new(InvalidPasswordResetCode, "The password-reset code is invalid or has expired.", ErrorType.Unprocessable);

    public static Error PasswordResetRateLimited() =>
        new(PasswordResetRateLimitedCode, "Too many password-reset requests. Try again later.", ErrorType.TooManyRequests);

    public static Error CaptchaFailed() =>
        new(CaptchaFailedCode, "The CAPTCHA check failed. Try again.", ErrorType.Unprocessable);

    public static Error CaptchaUnavailable() =>
        new(CaptchaUnavailableCode, "The CAPTCHA service is unavailable. Try again later.", ErrorType.Unprocessable);
}
