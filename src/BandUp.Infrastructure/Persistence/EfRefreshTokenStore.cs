using System.Data;
using BandUp.Application.Features.Identity;
using BandUp.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace BandUp.Infrastructure.Persistence;

public sealed class EfRefreshTokenStore(BandUpDbContext context) : IRefreshTokenStore
{
    public Task<RefreshToken?> FindByTokenHashAsync(string tokenHash, CancellationToken cancellationToken) =>
        context.RefreshTokens.AsNoTracking()
            .FirstOrDefaultAsync(token => token.TokenHash == tokenHash, cancellationToken);

    public async Task<RefreshSession?> FindSessionForUpdateAsync(
        string tokenHash,
        CancellationToken cancellationToken)
    {
        RequireTransaction();

        Guid? userId = await context.RefreshTokens
            .Where(token => token.TokenHash == tokenHash)
            .Select(token => (Guid?)token.UserId)
            .SingleOrDefaultAsync(cancellationToken);

        if (userId is null)
        {
            return null;
        }

        User? user = await LockUserAsync(userId.Value, cancellationToken);

        if (user is null)
        {
            return null;
        }

        // The first lookup locates the lock owner only. Re-read after acquiring the lock so
        // a waiting request sees a preceding rotation, even if this context tracked old state.
        RefreshToken? fresh = await context.RefreshTokens.AsNoTracking()
            .SingleOrDefaultAsync(token => token.TokenHash == tokenHash, cancellationToken);

        return fresh is null ? null : new RefreshSession(user, TrackFresh(fresh));
    }

    public async Task<IReadOnlyList<RefreshToken>> ListUsableForUserAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        List<RefreshToken> tokens = await context.RefreshTokens
            .Where(token => token.UserId == userId && token.RevokedAt == null)
            .ToListAsync(cancellationToken);

        return tokens;
    }

    public async Task AddAsync(RefreshToken token, CancellationToken cancellationToken)
    {
        if (await LockUserAsync(token.UserId, cancellationToken) is null)
        {
            throw new InvalidOperationException("Cannot add a session for a missing user.");
        }

        await context.RefreshTokens.AddAsync(token, cancellationToken);
    }

    public async Task RevokeAllForUserAsync(
        Guid userId,
        DateTimeOffset revokedAt,
        CancellationToken cancellationToken)
    {
        if (await LockUserAsync(userId, cancellationToken) is null)
        {
            return;
        }

        List<RefreshToken> tokens = await context.RefreshTokens.AsNoTracking()
            .Where(token => token.UserId == userId && token.RevokedAt == null)
            .ToListAsync(cancellationToken);

        foreach (RefreshToken token in tokens)
        {
            TrackFresh(token).Revoke(revokedAt);
        }
    }

    public async Task RevokeForUserAsync(
        Guid userId,
        string tokenHash,
        DateTimeOffset revokedAt,
        CancellationToken cancellationToken)
    {
        if (await LockUserAsync(userId, cancellationToken) is null)
        {
            return;
        }

        RefreshToken? token = await context.RefreshTokens.AsNoTracking()
            .SingleOrDefaultAsync(
                token => token.UserId == userId && token.TokenHash == tokenHash,
                cancellationToken);

        if (token is not null)
        {
            TrackFresh(token).Revoke(revokedAt);
        }
    }

    private Task<User?> LockUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        RequireTransaction();

        // All session mutations lock the same user row first. No token-row lock is taken
        // before this one, so reuse, rotation, logout and login share a consistent order.
        return context.Users
            .FromSqlInterpolated($"SELECT * FROM identity.users WHERE id = {userId} FOR UPDATE")
            .AsNoTracking()
            .SingleOrDefaultAsync(cancellationToken);
    }

    private RefreshToken TrackFresh(RefreshToken fresh)
    {
        RefreshToken? tracked = context.RefreshTokens.Local.FirstOrDefault(token => token.Id == fresh.Id);

        if (tracked is null)
        {
            context.RefreshTokens.Attach(fresh);
            return fresh;
        }

        var entry = context.Entry(tracked);

        if (entry.State != EntityState.Unchanged)
        {
            throw new InvalidOperationException("Lock and re-read a session before changing it.");
        }

        entry.CurrentValues.SetValues(fresh);
        entry.OriginalValues.SetValues(fresh);
        return tracked;
    }

    private void RequireTransaction()
    {
        if (context.Database.CurrentTransaction?.GetDbTransaction().IsolationLevel != IsolationLevel.ReadCommitted)
        {
            throw new InvalidOperationException("Session mutation requires an open Read Committed transaction.");
        }
    }
}
