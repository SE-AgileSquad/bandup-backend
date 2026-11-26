namespace BandUp.Application.Features.Identity;

/// <summary>Atomic quotas count eligible and ineligible accounts alike, independently of database rollback.</summary>
public interface IOtpLoginRateLimiter
{
    Task<OtpLoginRateLimitDecision> AcquireRequestAsync(string email, string? clientIp, CancellationToken cancellationToken);

    Task<OtpLoginRateLimitDecision> AcquireVerificationAsync(string email, string? clientIp, CancellationToken cancellationToken);
}
