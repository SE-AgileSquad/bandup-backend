namespace BandUp.Application.Features.Identity;

/// <summary>Initial recovery limits; enforcement belongs to the later challenge and rate-limit adapters.</summary>
public static class PasswordResetPolicy
{
    public const int CodeLength = 6;
    public const int MaxFailedAttempts = 5;
    public const int MaxRequestsPerEmail = 5;
    public const int MaxRequestsPerIp = 20;
    public const int MaxVerificationAttemptsPerEmail = 20;
    public const int MaxVerificationAttemptsPerIp = 100;

    public static TimeSpan CodeLifetime { get; } = TimeSpan.FromMinutes(10);
    public static TimeSpan ResendCooldown { get; } = TimeSpan.FromMinutes(1);
    public static TimeSpan RequestWindow { get; } = TimeSpan.FromHours(1);
    public static TimeSpan VerificationWindow { get; } = TimeSpan.FromMinutes(15);
}
