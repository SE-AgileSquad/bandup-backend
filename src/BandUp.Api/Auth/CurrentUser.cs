using System.Security.Claims;
using BandUp.Application.Abstractions;
using BandUp.Application.Features.Identity;

namespace BandUp.Api.Auth;

/// <summary>
/// Reads the caller from the request the authentication handler populated. Registered by the
/// API, which is the only host that has an <c>HttpContext</c>. Claim names follow
/// <see cref="IdentityClaimTypes"/> first, falling back to the long CLR claim types for
/// handlers that map inbound claims.
/// </summary>
public sealed class CurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    private ClaimsPrincipal? Principal => accessor.HttpContext?.User;

    public Guid? UserId =>
        Guid.TryParse(FindValue(IdentityClaimTypes.Subject, ClaimTypes.NameIdentifier), out Guid id)
            ? id
            : null;

    public string? Role => FindValue(IdentityClaimTypes.Role, ClaimTypes.Role);

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated ?? false;

    private string? FindValue(params string[] claimTypes)
    {
        ClaimsPrincipal? principal = Principal;

        if (principal is null)
        {
            return null;
        }

        foreach (string claimType in claimTypes)
        {
            string? value = principal.FindFirstValue(claimType);

            if (value is not null)
            {
                return value;
            }
        }

        return null;
    }
}