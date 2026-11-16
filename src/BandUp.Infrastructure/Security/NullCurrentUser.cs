using BandUp.Application.Abstractions;
using BandUp.Application.Features.Identity;

namespace BandUp.Infrastructure.Security;

/// <summary>
/// The default registration, used by the Worker and by any host without an HTTP request.
/// </summary>
public sealed class NullCurrentUser : ICurrentUser
{
    public Guid? UserId => null;

    public string? Role => null;

    public bool IsAuthenticated => false;

    public AccessTokenIdentity? AccessToken => null;
}
