using BandUp.Domain.Common;

namespace BandUp.Domain.Identity;

/// <summary>
/// An opaque refresh token. Only the SHA-256 hash of the value the client holds is stored, so a
/// database leak cannot be replayed against the API.
/// </summary>
public sealed class RefreshToken : Entity
{
    private RefreshToken()
    {
    }

    private RefreshToken(
        Guid id,
        Guid userId,
        string tokenHash,
        DateTimeOffset createdAt,
        DateTimeOffset expiresAt,
        string? userAgent)
        : base(id)
    {
        UserId = userId;
        TokenHash = tokenHash;
        CreatedAt = createdAt;
        ExpiresAt = expiresAt;
        UserAgent = userAgent;
    }

    public Guid UserId { get; private set; }

    public string TokenHash { get; private set; } = null!;

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    public DateTimeOffset? RevokedAt { get; private set; }

    public Guid? ReplacedById { get; private set; }

    public string? UserAgent { get; private set; }

    public static RefreshToken Issue(
        Guid userId,
        string tokenHash,
        DateTimeOffset createdAt,
        DateTimeOffset expiresAt,
        string? userAgent) =>
        new(Guid.CreateVersion7(), userId, tokenHash, createdAt, expiresAt, userAgent);

    public bool IsUsable(DateTimeOffset now) => RevokedAt is null && ExpiresAt > now;

    public bool IsExpired(DateTimeOffset now) => ExpiresAt <= now;

    /// <summary>
    /// Marks this token as used and points it at its successor. Revoking an already-revoked
    /// token is a programming error, not an expected failure: the caller checks usability first.
    /// </summary>
    public void Rotate(RefreshToken successor)
    {
        if (RevokedAt is not null)
        {
            throw new DomainException($"Refresh token {Id} was already revoked.");
        }

        RevokedAt = successor.CreatedAt;
        ReplacedById = successor.Id;
    }

    public void Revoke(DateTimeOffset at) => RevokedAt ??= at;
}