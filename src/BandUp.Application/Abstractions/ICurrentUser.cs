using BandUp.Application.Features.Identity;

namespace BandUp.Application.Abstractions;

/// <summary>
/// The authenticated caller of the current operation. Nulls mean anonymous: the Worker has no
/// request and therefore no user.
/// </summary>
public interface ICurrentUser
{
    Guid? UserId { get; }

    string? Role { get; }

    bool IsAuthenticated { get; }

    /// <summary>Validated session claims; null for anonymous callers or missing/malformed claims.</summary>
    AccessTokenIdentity? AccessToken { get; }
}
