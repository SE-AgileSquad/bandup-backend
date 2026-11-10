namespace BandUp.Application.Abstractions;

/// <summary>
/// Verifies a CAPTCHA token on the server. The client IP is part of the contract because the
/// outage fallback (AUTH-05) is a per-IP attempt budget, not a global one.
/// </summary>
public interface ICaptchaVerifier
{
    Task<CaptchaOutcome> VerifyAsync(
        string? token,
        string? clientIp,
        CancellationToken cancellationToken);
}