namespace BandUp.Application.Features.Identity;

public enum OtpLoginRateLimitDecision
{
    Allowed,
    AccountSuppressed,
    ClientRateLimited,
}
