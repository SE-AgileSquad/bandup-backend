namespace BandUp.Application.Features.Identity;

/// <summary>Atomic quotas count eligible and ineligible accounts alike, independently of database rollback.</summary>
public interface IPasswordResetRateLimiter
{
    Task<PasswordResetRateLimitDecision> AcquireRequestAsync(string email, string? clientIp, CancellationToken cancellationToken);

    Task<PasswordResetRateLimitDecision> AcquireVerificationAsync(string email, string? clientIp, CancellationToken cancellationToken);
}
