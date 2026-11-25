namespace BandUp.Application.Features.Identity;

public enum PasswordResetRateLimitDecision
{
    Allowed,
    AccountSuppressed,
    ClientRateLimited,
}
