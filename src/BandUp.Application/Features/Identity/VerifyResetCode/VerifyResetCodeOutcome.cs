namespace BandUp.Application.Features.Identity.VerifyResetCode;

/// <summary>Rejected is committable so incorrect-guess counters survive the public 422 response.</summary>
public abstract record VerifyResetCodeOutcome
{
    private VerifyResetCodeOutcome() { }
    public sealed record Verified(string ResetToken, DateTimeOffset ExpiresAt) : VerifyResetCodeOutcome;
    public sealed record Rejected : VerifyResetCodeOutcome;
}
