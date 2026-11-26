using System.Data;
using BandUp.Application.Features.Identity;
using BandUp.Domain.Common;
using BandUp.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace BandUp.Infrastructure.Persistence;

public sealed class EfOtpLoginChallengeStore(BandUpDbContext context) : IOtpLoginChallengeStore
{
    public async Task<OtpLoginSession?> FindForUpdateAsync(string email, CancellationToken cancellationToken)
    {
        RequireTransaction();
        // Raw SQL parameters otherwise use text mapping; cast to retain citext email semantics.
        User? user = await context.Users
            .FromSqlInterpolated($"SELECT * FROM identity.users WHERE email = {email}::citext FOR UPDATE")
            .AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        if (user is null)
        {
            return null;
        }

        if (context.ChangeTracker.Entries<OtpLoginChallenge>()
            .Any(entry => entry.Entity.UserId == user.Id && entry.State != EntityState.Unchanged))
        {
            throw new InvalidOperationException("Lock and re-read OTP sign-in state before changing it.");
        }

        OtpLoginChallenge? challenge = await context.OtpLoginChallenges.AsNoTracking()
            .Where(candidate => candidate.UserId == user.Id)
            .OrderByDescending(candidate => candidate.CreatedAt).ThenByDescending(candidate => candidate.Id)
            .FirstOrDefaultAsync(cancellationToken);

        return new OtpLoginSession(TrackFresh(user), challenge is null ? null : TrackFresh(challenge));
    }

    public async Task ReplaceAsync(OtpLoginChallenge challenge, CancellationToken cancellationToken)
    {
        RequireTransaction();
        if (!challenge.IsUsable(challenge.CreatedAt) || challenge.FailedAttempts != 0 ||
            context.Entry(challenge).State != EntityState.Detached)
        {
            throw new InvalidOperationException("Only a new, unused challenge can replace outstanding challenges.");
        }

        if (!await LockUserAsync(challenge.UserId, cancellationToken))
        {
            throw new InvalidOperationException("Cannot add a challenge for a missing user.");
        }

        await InvalidateOpenAsync(challenge.UserId, null, challenge.CreatedAt, cancellationToken);
        await context.OtpLoginChallenges.AddAsync(challenge, cancellationToken);
    }

    public async Task InvalidateOthersAsync(
        Guid userId, Guid retainedChallengeId, DateTimeOffset invalidatedAt, CancellationToken cancellationToken)
    {
        RequireTransaction();
        if (retainedChallengeId == Guid.Empty)
        {
            throw new ArgumentException("A retained challenge id is required.", nameof(retainedChallengeId));
        }

        if (await LockUserAsync(userId, cancellationToken))
        {
            await InvalidateOpenAsync(userId, retainedChallengeId, invalidatedAt, cancellationToken);
        }
    }

    public async Task InvalidateAllForUserAsync(Guid userId, DateTimeOffset invalidatedAt, CancellationToken cancellationToken)
    {
        RequireTransaction();
        if (await LockUserAsync(userId, cancellationToken))
        {
            await InvalidateOpenAsync(userId, null, invalidatedAt, cancellationToken);
        }
    }

    private async Task InvalidateOpenAsync(
        Guid userId, Guid? excludedId, DateTimeOffset at, CancellationToken cancellationToken)
    {
        if (at.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Challenge invalidation requires a UTC timestamp.", nameof(at));
        }

        var tracked = context.ChangeTracker.Entries<OtpLoginChallenge>()
            .Where(entry => entry.Entity.UserId == userId && entry.Entity.Id != excludedId).ToArray();
        if (tracked.Any(entry => entry.State != EntityState.Unchanged))
        {
            throw new InvalidOperationException("Save pending challenge writes before replacing or invalidating other challenges.");
        }

        // Execute the update before staging an insertion so the partial unique index is never
        // temporarily violated. Both operations remain in the caller's transaction.
        await context.OtpLoginChallenges
            .Where(challenge => challenge.UserId == userId && (excludedId == null || challenge.Id != excludedId.Value) &&
                challenge.ConsumedAt == null && challenge.InvalidatedAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(challenge => challenge.InvalidatedAt, at), cancellationToken);

        foreach (var entry in tracked)
        {
            await entry.ReloadAsync(cancellationToken);
        }
    }

    private async Task<bool> LockUserAsync(Guid userId, CancellationToken cancellationToken) =>
        await context.Users.FromSqlInterpolated($"SELECT * FROM identity.users WHERE id = {userId} FOR UPDATE")
            .AsNoTracking().SingleOrDefaultAsync(cancellationToken) is not null;

    private T TrackFresh<T>(T fresh) where T : Entity
    {
        T? tracked = context.Set<T>().Local.FirstOrDefault(entity => entity.Id == fresh.Id);
        if (tracked is null)
        {
            context.Attach(fresh);
            return fresh;
        }

        var entry = context.Entry(tracked);
        if (entry.State != EntityState.Unchanged)
        {
            throw new InvalidOperationException("Lock and re-read OTP sign-in state before changing it.");
        }

        entry.CurrentValues.SetValues(fresh);
        entry.OriginalValues.SetValues(fresh);
        return tracked;
    }

    private void RequireTransaction()
    {
        if (context.Database.CurrentTransaction?.GetDbTransaction().IsolationLevel != IsolationLevel.ReadCommitted)
        {
            throw new InvalidOperationException("OTP sign-in storage requires an open Read Committed transaction.");
        }
    }
}
