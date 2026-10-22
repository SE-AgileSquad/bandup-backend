namespace BandUp.Application.Common;

public enum ErrorType
{
    Validation,
    Unauthorized,
    PaymentRequired,
    Forbidden,
    NotFound,
    Conflict,
    Unprocessable,
    TooManyRequests,
    Unavailable,
    Unexpected,
}