using System.Globalization;
using System.Security.Claims;
using BandUp.Application.Features.Identity;

namespace BandUp.Api.Auth;

internal static class AccessTokenClaims
{
    // Call only on the principal produced by authentication, never on a decoded raw token.
    public static AccessTokenIdentity? Read(ClaimsPrincipal? principal)
    {
        if (principal?.Identity?.IsAuthenticated != true)
        {
            return null;
        }

        string? subject = SingleValue(principal, IdentityClaimTypes.Subject, ClaimTypes.NameIdentifier);
        string? jwtId = SingleValue(principal, IdentityClaimTypes.JwtId);
        string? expiry = SingleValue(principal, IdentityClaimTypes.Expiry);

        if (!Guid.TryParse(subject, out Guid userId) || userId == Guid.Empty ||
            !Guid.TryParse(jwtId, out Guid tokenId) || tokenId == Guid.Empty ||
            !long.TryParse(expiry, NumberStyles.None, CultureInfo.InvariantCulture, out long seconds))
        {
            return null;
        }

        try
        {
            return new AccessTokenIdentity(userId, tokenId, DateTimeOffset.FromUnixTimeSeconds(seconds));
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    private static string? SingleValue(ClaimsPrincipal principal, params string[] types)
    {
        Claim[] claims = principal.Claims.Where(claim => types.Contains(claim.Type, StringComparer.Ordinal)).Take(2).ToArray();
        return claims.Length == 1 ? claims[0].Value : null;
    }
}
