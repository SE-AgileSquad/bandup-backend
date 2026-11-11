namespace BandUp.Application.Features.Identity;

/// <summary>
/// The claim names the access token carries. Kept in Application so token issuance and
/// validation cannot drift: both sides reference these constants, never raw strings.
/// </summary>
public static class IdentityClaimTypes
{
    public const string Subject = "sub";

    public const string Role = "role";

    public const string JwtId = "jti";

    public const string Expiry = "exp";
}