using BandUp.Domain.Common;

namespace BandUp.Domain.Identity;

/// <summary>A reset-only authorization issued by consuming an email challenge.</summary>
public sealed class PasswordResetToken : Entity
{
    private PasswordResetToken() { }

    private PasswordResetToken(PasswordResetChallenge challenge, string tokenHash, DateTimeOffset createdAt, DateTimeOffset expiresAt)
        : base(Guid.CreateVersion7())
    {
        UserId = challenge.UserId;
        ChallengeId = challenge.Id;
        TokenHash = tokenHash;
        CreatedAt = createdAt;
        ExpiresAt = expiresAt;
    }

    public Guid UserId { get; private set; }
    public Guid ChallengeId { get; private set; }
    public string TokenHash { get; private set; } = null!;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset? ConsumedAt { get; private set; }
    public DateTimeOffset? InvalidatedAt { get; private set; }
    public uint Version { get; private set; }

    public static PasswordResetToken Issue(
        PasswordResetChallenge challenge, string tokenHash, DateTimeOffset createdAt, DateTimeOffset expiresAt)
    {
        RequireUtc(createdAt);
        RequireUtc(expiresAt);
        if (challenge.ConsumedAt != createdAt || challenge.InvalidatedAt is not null || expiresAt <= createdAt ||
            tokenHash is not { Length: 64 } || !tokenHash.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f'))
        {
            throw new DomainException("A fresh consumed challenge, protected token and valid lifetime are required.");
        }
        return new PasswordResetToken(challenge, tokenHash, createdAt, expiresAt);
    }

    public bool IsUsable(DateTimeOffset now) =>
        now >= CreatedAt && now < ExpiresAt && ConsumedAt is null && InvalidatedAt is null;

    public void Consume(DateTimeOffset now)
    {
        RequireUtc(now);
        if (!IsUsable(now)) { throw new DomainException("Check reset-token usability before consumption."); }
        ConsumedAt = now;
    }

    public void Invalidate(DateTimeOffset now)
    {
        RequireUtc(now);
        if (now < CreatedAt) { throw new DomainException("A reset token cannot be invalidated before creation."); }
        if (ConsumedAt is null) { InvalidatedAt ??= now; }
    }

    private static void RequireUtc(DateTimeOffset value)
    {
        if (value.Offset != TimeSpan.Zero) { throw new DomainException("Reset-token timestamps must be UTC."); }
    }
}
