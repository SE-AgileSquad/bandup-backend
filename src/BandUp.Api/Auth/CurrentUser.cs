using System.Security.Claims;
using BandUp.Application.Abstractions;

namespace BandUp.Api.Auth;

/// <summary>
/// Reads the caller from the request the authentication handler populated. Registered by the
/// API, which is the only host that has an <c>HttpContext</c>.
/// </summary>
public sealed class CurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    private ClaimsPrincipal? Principal => accessor.HttpContext?.User;

    public Guid? UserId =>
        Guid.TryParse(Principal?.FindFirstValue(ClaimTypes.NameIdentifier), out Guid id) ? id : null;

    public string? Role => Principal?.FindFirstValue(ClaimTypes.Role);

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated ?? false;
}