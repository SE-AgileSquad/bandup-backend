using System.Data;
using BandUp.Application.Features.Identity;
using BandUp.Domain.Common;
using BandUp.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace BandUp.Infrastructure.Persistence;

public sealed class EfPasswordResetTokenStore(BandUpDbContext context) : IPasswordResetTokenStore
{
    public async Task<PasswordResetTokenSession?> FindForUpdateAsync(string tokenHash, CancellationToken cancellationToken)
    {
        RequireTransaction();
        Guid? userId = await context.PasswordResetTokens.Where(token => token.TokenHash == tokenHash)
            .Select(token => (Guid?)token.UserId).SingleOrDefaultAsync(cancellationToken);
        if (userId is null) { return null; }
        User? user = await LockUserAsync(userId.Value, cancellationToken);
        if (user is null) { return null; }
        PasswordResetToken? token = await context.PasswordResetTokens.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.TokenHash == tokenHash, cancellationToken);
        if (token is null) { return null; }
        // Supersession is enforced even when a caller replaced a challenge directly through its store.
        var challenge = await context.PasswordResetChallenges.AsNoTracking().Where(candidate => candidate.UserId == user.Id)
            .OrderByDescending(candidate => candidate.CreatedAt).ThenByDescending(candidate => candidate.Id)
            .Select(candidate => new { candidate.Id, candidate.ConsumedAt, candidate.InvalidatedAt }).FirstOrDefaultAsync(cancellationToken);
        if (challenge is null || challenge.Id != token.ChallengeId || challenge.ConsumedAt is null || challenge.InvalidatedAt is not null)
        {
            return null;
        }
        return new PasswordResetTokenSession(TrackFresh(user), TrackFresh(token));
    }

    public async Task ReplaceAsync(PasswordResetToken token, CancellationToken cancellationToken)
    {
        RequireTransaction();
        if (!token.IsUsable(token.CreatedAt) || context.Entry(token).State != EntityState.Detached)
        {
            throw new InvalidOperationException("Only a new, unused reset token can be staged.");
        }
        if (await LockUserAsync(token.UserId, cancellationToken) is null) { throw new InvalidOperationException("Cannot authorize a missing user."); }
        await InvalidateOpenAsync(token.UserId, null, token.CreatedAt, cancellationToken);
        await context.PasswordResetTokens.AddAsync(token, cancellationToken);
    }

    public async Task InvalidateAllForUserAsync(Guid userId, DateTimeOffset at, CancellationToken cancellationToken)
    {
        RequireTransaction();
        if (await LockUserAsync(userId, cancellationToken) is not null) { await InvalidateOpenAsync(userId, null, at, cancellationToken); }
    }

    public async Task InvalidateOthersAsync(Guid userId, Guid retainedTokenId, DateTimeOffset at, CancellationToken cancellationToken)
    {
        RequireTransaction();
        if (retainedTokenId == Guid.Empty) { throw new ArgumentException("A retained token id is required.", nameof(retainedTokenId)); }
        if (await LockUserAsync(userId, cancellationToken) is not null) { await InvalidateOpenAsync(userId, retainedTokenId, at, cancellationToken); }
    }

    private async Task InvalidateOpenAsync(Guid userId, Guid? excludedId, DateTimeOffset at, CancellationToken cancellationToken)
    {
        if (at.Offset != TimeSpan.Zero) { throw new ArgumentException("Reset-token invalidation requires UTC.", nameof(at)); }
        var tracked = context.ChangeTracker.Entries<PasswordResetToken>()
            .Where(entry => entry.Entity.UserId == userId && entry.Entity.Id != excludedId).ToArray();
        if (tracked.Any(entry => entry.State != EntityState.Unchanged)) { throw new InvalidOperationException("Save pending reset-token writes before invalidating other tokens."); }
        await context.PasswordResetTokens.Where(token => token.UserId == userId && (excludedId == null || token.Id != excludedId.Value) &&
            token.ConsumedAt == null && token.InvalidatedAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(token => token.InvalidatedAt, at), cancellationToken);
        foreach (var entry in tracked) { await entry.ReloadAsync(cancellationToken); }
    }

    private async Task<User?> LockUserAsync(Guid userId, CancellationToken cancellationToken) =>
        await context.Users.FromSqlInterpolated($"SELECT * FROM identity.users WHERE id = {userId} FOR UPDATE")
            .AsNoTracking().SingleOrDefaultAsync(cancellationToken);

    private T TrackFresh<T>(T fresh) where T : Entity
    {
        T? tracked = context.Set<T>().Local.FirstOrDefault(entity => entity.Id == fresh.Id);
        if (tracked is null) { context.Attach(fresh); return fresh; }
        var entry = context.Entry(tracked);
        if (entry.State != EntityState.Unchanged) { throw new InvalidOperationException("Lock and re-read reset authorization before changing it."); }
        entry.CurrentValues.SetValues(fresh);
        entry.OriginalValues.SetValues(fresh);
        return tracked;
    }

    private void RequireTransaction()
    {
        if (context.Database.CurrentTransaction?.GetDbTransaction().IsolationLevel != IsolationLevel.ReadCommitted)
        {
            throw new InvalidOperationException("Reset-token storage requires an open Read Committed transaction.");
        }
    }
}
