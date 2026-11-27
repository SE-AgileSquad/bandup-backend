namespace BandUp.Infrastructure.Configuration;

public sealed class OtpLoginSettings
{
    public const string SectionName = "OtpLogin";

    /// <summary>Base64-encoded random key of at least 32 bytes, independent of JWT and password-reset keys.</summary>
    public string? CodeProtectionKey { get; set; }

    public string RateLimitProvider { get; set; } = ServiceProviders.Redis;
}
