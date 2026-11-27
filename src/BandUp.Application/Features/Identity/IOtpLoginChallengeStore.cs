using BandUp.Domain.Identity;

namespace BandUp.Application.Features.Identity;

/// <summary>Operations require an open Read Committed transaction and never commit it.</summary>
public interface IOtpLoginChallengeStore
{
    /// <summary>
    /// Locks the user before re-reading their latest challenge. Returns null only for a missing
    /// user; existing users without a challenge return a session with Challenge=null.
    /// Call before inspecting/changing state. Dirty tracked state is rejected rather than overwritten.
    /// </summary>
    Task<OtpLoginSession?> FindForUpdateAsync(string email, CancellationToken cancellationToken);

    /// <summary>
    /// Locks the user, immediately invalidates outstanding challenges inside the caller's transaction,
    /// and stages the new challenge. The caller must save and commit. No pending challenge writes are allowed.
    /// </summary>
    Task ReplaceAsync(OtpLoginChallenge challenge, CancellationToken cancellationToken);

    /// <summary>
    /// Invalidates outstanding rows except the supplied challenge, preserving its staged consumption.
    /// Bulk updates are part of the caller's transaction and roll back with it.
    /// </summary>
    Task InvalidateOthersAsync(
        Guid userId, Guid retainedChallengeId, DateTimeOffset invalidatedAt, CancellationToken cancellationToken);

    /// <summary>Invalidates all outstanding login codes in the caller's transaction after a password reset.</summary>
    Task InvalidateAllForUserAsync(Guid userId, DateTimeOffset invalidatedAt, CancellationToken cancellationToken);
}
