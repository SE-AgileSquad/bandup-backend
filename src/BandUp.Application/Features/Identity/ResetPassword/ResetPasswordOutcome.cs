namespace BandUp.Application.Features.Identity.ResetPassword;

/// <summary>
/// A completed recovery action. Rejected attempts that update counters must return a successful
/// Result containing Rejected so the transaction commits before the API returns the public error.
/// </summary>
public abstract record ResetPasswordOutcome
{
    private ResetPasswordOutcome()
    {
    }

    public sealed record Completed : ResetPasswordOutcome;

    /// <summary>Maps to INVALID_PASSWORD_RESET_CODE after the attempt state has committed.</summary>
    public sealed record Rejected : ResetPasswordOutcome;
}
