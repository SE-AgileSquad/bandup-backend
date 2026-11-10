namespace BandUp.Application.Abstractions;

/// <summary>
/// The three results a CAPTCHA check can have. The distinction between the last two is the whole
/// point: a wrong token must always be rejected, while an unreachable provider is what triggers
/// the AUTH-05 fallback.
/// </summary>
public enum CaptchaOutcome
{
    Valid,

    /// <summary>The provider answered and said the token is bad.</summary>
    InvalidToken,

    /// <summary>The provider could not be reached. Not the same as an invalid token.</summary>
    ProviderUnavailable,
}