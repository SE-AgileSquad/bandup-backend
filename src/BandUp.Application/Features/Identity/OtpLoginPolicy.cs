namespace BandUp.Application.Features.Identity;

/// <summary>AUTH-16: limits for email sign-in, independent of password recovery.</summary>
public static class OtpLoginPolicy
{
    public const string ProtectionPurpose = "bandup:otp-login:v1";
    public const int CodeLength = 6;
    public const int MaxCaptchaTokenLength = 2048;
    public const int MaxFailedAttempts = 5;
    public const int MaxRequestsPerEmail = 5;
    public const int MaxRequestsPerIp = 20;
    public const int MaxVerificationAttemptsPerEmail = 20;
    public const int MaxVerificationAttemptsPerIp = 100;

    public static TimeSpan CodeLifetime { get; } = TimeSpan.FromMinutes(10);
    public static TimeSpan ResendCooldown { get; } = TimeSpan.FromMinutes(1);
    public static TimeSpan RequestWindow { get; } = TimeSpan.FromHours(1);
    public static TimeSpan VerificationWindow { get; } = TimeSpan.FromMinutes(15);
    public static TimeSpan MinimumRequestDuration { get; } = TimeSpan.FromMilliseconds(250);
}
