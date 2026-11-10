namespace BandUp.Infrastructure.Configuration;

public sealed class CaptchaSettings
{
    public const string SectionName = "Captcha";

    /// <summary>
    /// Turnstile site key. Public by design; the same value is embedded in the frontend page.
    /// </summary>
    public string? SiteKey { get; set; }

    /// <summary>
    /// Turnstile secret key. Supplied by user secrets or the environment, never by a settings
    /// file. Required when the provider is Turnstile.
    /// </summary>
    public string? SecretKey { get; set; }

    /// <summary>
    /// How long the outage fallback remembers one client IP, in minutes.
    /// </summary>
    public int FallbackWindowMinutes { get; set; } = 60;

    /// <summary>
    /// How many attempts one client IP gets while the provider is unreachable before
    /// CAPTCHA_UNAVAILABLE is returned instead (AUTH-05).
    /// </summary>
    public int FallbackMaxAttempts { get; set; } = 3;
}
