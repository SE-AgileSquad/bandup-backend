using BandUp.Domain.Common;

namespace BandUp.Domain.Identity;

public sealed class PasswordResetChallenge : Entity
{
    private PasswordResetChallenge()
    {
    }

    private PasswordResetChallenge(
        Guid id, Guid userId, string codeMac, DateTimeOffset createdAt,
        DateTimeOffset expiresAt, int maxFailedAttempts) : base(id)
    {
        UserId = userId;
        CodeMac = codeMac;
        CreatedAt = createdAt;
        ExpiresAt = expiresAt;
        MaxFailedAttempts = maxFailedAttempts;
    }

    public Guid UserId { get; private set; }
    public string CodeMac { get; private set; } = null!;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public int MaxFailedAttempts { get; private set; }
    public int FailedAttempts { get; private set; }
    public DateTimeOffset? ConsumedAt { get; private set; }
    public DateTimeOffset? InvalidatedAt { get; private set; }

    /// <summary>Optimistic concurrency version populated by persistence.</summary>
    public uint Version { get; private set; }

    /// <summary>The caller allocates the id before computing the challenge-bound code MAC.</summary>
    public static PasswordResetChallenge Issue(
        Guid id, Guid userId, string codeMac, DateTimeOffset createdAt,
        DateTimeOffset expiresAt, int maxFailedAttempts)
    {
        RequireUtc(createdAt);
        RequireUtc(expiresAt);
        if (id == Guid.Empty || userId == Guid.Empty || expiresAt <= createdAt ||
            maxFailedAttempts is < 1 or > 100 || codeMac is not { Length: 64 } ||
            !codeMac.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f'))
        {
            throw new DomainException("Invalid password-reset challenge parameters.");
        }

        return new PasswordResetChallenge(id, userId, codeMac, createdAt, expiresAt, maxFailedAttempts);
    }

    public bool IsUsable(DateTimeOffset now) =>
        now >= CreatedAt && now < ExpiresAt && FailedAttempts < MaxFailedAttempts &&
        ConsumedAt is null && InvalidatedAt is null;

    public void RecordFailedAttempt(DateTimeOffset now)
    {
        RequireUsable(now);
        FailedAttempts++;
    }

    public void Consume(DateTimeOffset now)
    {
        RequireUsable(now);
        ConsumedAt = now;
    }

    public void Invalidate(DateTimeOffset now)
    {
        RequireUtc(now);
        if (now < CreatedAt)
        {
            throw new DomainException("A challenge cannot be invalidated before it was created.");
        }

        if (ConsumedAt is null)
        {
            InvalidatedAt ??= now;
        }
    }

    private void RequireUsable(DateTimeOffset now)
    {
        RequireUtc(now);
        if (!IsUsable(now))
        {
            throw new DomainException("Check challenge usability before recording or consuming an attempt.");
        }
    }

    private static void RequireUtc(DateTimeOffset value)
    {
        if (value.Offset != TimeSpan.Zero)
        {
            throw new DomainException("Password-reset challenge timestamps must be UTC.");
        }
    }
}
