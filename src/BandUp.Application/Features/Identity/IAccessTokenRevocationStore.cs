namespace BandUp.Application.Features.Identity;

/// <summary>Access-token revocation keyed by a validated JWT id, never by its raw value.</summary>
public interface IAccessTokenRevocationStore
{
    /// <summary>
    /// Retains the entry through the token's expiry and validation clock skew. Write failures
    /// propagate; callers must not claim logout succeeded when this operation fails.
    /// </summary>
    Task RevokeAsync(Guid tokenId, DateTimeOffset expiresAt, CancellationToken cancellationToken);

    /// <summary>Redis read outages log a warning and fail open, as specified in ADR 0016.</summary>
    Task<bool> IsRevokedAsync(Guid tokenId, CancellationToken cancellationToken);
}
