namespace BandUp.Application.Features.Identity.Refresh;

/// <summary>
/// A completed refresh operation. Rejection after revocation is a successful security action:
/// the transaction must commit before the API returns INVALID_REFRESH_TOKEN. Other rejections
/// use a failed Result, which rolls back. See ADR 0016.
/// </summary>
public abstract record RefreshOutcome
{
    private RefreshOutcome()
    {
    }

    /// <summary>Tokens for the API to write as cookies after commit, never as a JSON body.</summary>
    public sealed record Issued(
        AccessToken AccessToken,
        string RefreshToken,
        DateTimeOffset RefreshTokenExpiresAt) : RefreshOutcome;

    /// <summary>All unrevoked refresh tokens for the user have been revoked after reuse.</summary>
    public sealed record RejectedAfterRevocation : RefreshOutcome;
}
