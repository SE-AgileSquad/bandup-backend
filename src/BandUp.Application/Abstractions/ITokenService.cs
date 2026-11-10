using BandUp.Application.Features.Identity;

namespace BandUp.Application.Abstractions;

/// <summary>
/// Issues the two tokens a session is built from. Access tokens are short-lived JWTs; refresh
/// tokens are opaque random values whose hash is what gets stored.
/// </summary>
public interface ITokenService
{
    AccessToken CreateAccessToken(Guid userId, string role, DateTimeOffset now);

    /// <summary>
    /// A cryptographically random refresh token. The caller stores only its hash.
    /// </summary>
    string GenerateRefreshToken();

    /// <summary>
    /// The one-way digest of a raw refresh token, the only form persisted.
    /// </summary>
    string HashRefreshToken(string rawToken);
}