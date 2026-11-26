namespace BandUp.Application.Features.Identity.ForgotPassword;

/// <summary>The same acknowledgement for delivery, an ineligible account, or email-specific suppression.</summary>
public sealed record ForgotPasswordOutcome
{
    public string Message => "If an eligible account exists for this email, we'll send a password-reset code.";
}
