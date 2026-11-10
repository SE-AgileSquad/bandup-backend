using BandUp.Application.Abstractions;
using BandUp.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;

namespace BandUp.Infrastructure.Security;

/// <summary>
/// Implements AUTH-05. When the real provider cannot be reached, the request is allowed only
/// while the client's IP has fewer than the configured attempts in the fallback window; beyond
/// that the request is refused with CAPTCHA_UNAVAILABLE. An invalid token from a healthy
/// provider is never forgiven — only unreachability triggers the fallback.
/// </summary>
public sealed class OutageTolerantCaptchaVerifier(
    ICaptchaVerifier inner,
    ICacheService cache,
    CaptchaSettings settings,
    ILogger<OutageTolerantCaptchaVerifier> logger) : ICaptchaVerifier
{
    private const string KeyPrefix = "captcha:fallback:";

    public async Task<CaptchaOutcome> VerifyAsync(
        string? token,
        string? clientIp,
        CancellationToken cancellationToken)
    {
        CaptchaOutcome outcome = await inner.VerifyAsync(token, clientIp, cancellationToken);

        if (outcome is not CaptchaOutcome.ProviderUnavailable)
        {
            return outcome;
        }

        string key = KeyPrefix + (clientIp ?? "unknown");

        int? attempts = await cache.GetAsync<int>(key, cancellationToken);

        if (attempts is null)
        {
            // First attempt in the window. Count it and let the request through: one missed
            // CAPTCHA beats locking every registration out during a brief provider blip.
            logger.LogWarning(
                "CAPTCHA provider is unreachable; allowing request from {clientIp} under the outage fallback",
                clientIp ?? "unknown");

            await cache.SetAsync(
                key,
                1,
                TimeSpan.FromMinutes(settings.FallbackWindowMinutes),
                cancellationToken);

            return CaptchaOutcome.Valid;
        }

        if (attempts < settings.FallbackMaxAttempts)
        {
            logger.LogWarning(
                "CAPTCHA provider is unreachable; attempt {attempts} of {maxAttempts} for {clientIp}",
                attempts + 1,
                settings.FallbackMaxAttempts,
                clientIp ?? "unknown");

            await cache.SetAsync(
                key,
                attempts + 1,
                TimeSpan.FromMinutes(settings.FallbackWindowMinutes),
                cancellationToken);

            return CaptchaOutcome.Valid;
        }

        logger.LogWarning(
            "CAPTCHA provider is unreachable and {clientIp} has spent its {maxAttempts} fallback attempts",
            clientIp ?? "unknown",
            settings.FallbackMaxAttempts);

        return CaptchaOutcome.ProviderUnavailable;
    }
}
