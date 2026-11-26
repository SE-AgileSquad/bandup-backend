using BandUp.Application.Features.Identity.Login;

namespace BandUp.Application.Features.Identity.VerifyOtpLogin;

/// <summary>
/// Rejected must be returned inside a successful Result when guess state changed, so the
/// transaction commits before the API returns its public error. Completed carries internal
/// session credentials for cookie issuance after commit; never serialize it as API JSON.
/// </summary>
public abstract record VerifyOtpLoginOutcome
{
    private VerifyOtpLoginOutcome()
    {
    }

    public sealed record Completed(LoginResult Login) : VerifyOtpLoginOutcome;

    public sealed record Rejected : VerifyOtpLoginOutcome;
}
