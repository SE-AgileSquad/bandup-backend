using BandUp.Domain.Identity;

namespace BandUp.Application.Features.Identity;

/// <summary>
/// Session store for refresh tokens. Lookups are by the hash of the presented token, never by
/// the raw value.
/// </summary>
public interface IRefreshTokenStore
{
    /// <summary>A read-only snapshot, including revoked and expired credentials.</summary>
    Task<RefreshToken?> FindByTokenHashAsync(string tokenHash, CancellationToken cancellationToken);

    /// <summary>
    /// Requires an open Read Committed transaction. Locks the owner's sessions before re-reading the token
    /// and current user state. Always call this before inspecting or changing rotation state.
    /// </summary>
    Task<RefreshSession?> FindSessionForUpdateAsync(string tokenHash, CancellationToken cancellationToken);

    Task<IReadOnlyList<RefreshToken>> ListUsableForUserAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// Requires an open Read Committed transaction. Serializes insertion with rotation and revocation.
    /// </summary>
    Task AddAsync(RefreshToken token, CancellationToken cancellationToken);

    /// <summary>
    /// Requires an open Read Committed transaction. Locks the user's sessions and revokes every unrevoked
    /// row, including expired rows. Does not save or commit.
    /// </summary>
    Task RevokeAllForUserAsync(Guid userId, DateTimeOffset revokedAt, CancellationToken cancellationToken);

    /// <summary>
    /// Requires an open Read Committed transaction. Locks only the authenticated user's sessions. A missing,
    /// foreign or already-revoked token is a no-op. Does not save or commit.
    /// </summary>
    Task RevokeForUserAsync(
        Guid userId,
        string tokenHash,
        DateTimeOffset revokedAt,
        CancellationToken cancellationToken);
}
