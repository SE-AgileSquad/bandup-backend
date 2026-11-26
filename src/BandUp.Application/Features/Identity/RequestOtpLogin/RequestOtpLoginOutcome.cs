namespace BandUp.Application.Features.Identity.RequestOtpLogin;

/// <summary>Does not reveal account eligibility, delivery or per-email suppression.</summary>
public sealed record RequestOtpLoginOutcome
{
    public string Message => "If an eligible account exists for this email, we'll send a sign-in code.";
}
