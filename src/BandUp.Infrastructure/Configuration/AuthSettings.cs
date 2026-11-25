namespace BandUp.Infrastructure.Configuration;

public sealed class AuthSettings
{
    public const string SectionName = "Auth";

    // JWT validation and revocation retention must cover the same acceptance window.
    public static readonly TimeSpan AccessTokenClockSkew = TimeSpan.FromSeconds(30);

    /// <summary>
    /// HMAC key for the access token. Supplied by user secrets or the environment, never by a
    /// settings file. At least 32 characters.
    /// </summary>
    public string? SigningKey { get; set; }

    public string Issuer { get; set; } = "bandup";

    public string Audience { get; set; } = "bandup-api";

    public int AccessTokenMinutes { get; set; } = 15;

    public int RefreshTokenDays { get; set; } = 7;

    public string AccessCookieName { get; set; } = "bu_at";

    public string RefreshCookieName { get; set; } = "bu_rt";

    /// <summary>
    /// Whether cookies require HTTPS. Off in Development, where the API runs on plain HTTP.
    /// </summary>
    public bool RequireSecureCookies { get; set; } = true;
}
