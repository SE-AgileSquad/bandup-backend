using BandUp.Application.Features.Identity;
using BandUp.Domain.Identity;
using Microsoft.EntityFrameworkCore;

namespace BandUp.Infrastructure.Persistence;

public sealed class EfRefreshTokenStore(BandUpDbContext context) : IRefreshTokenStore
{
    public Task<RefreshToken?> FindByTokenHashAsync(string tokenHash, CancellationToken cancellationToken) =>
        context.RefreshTokens.FirstOrDefaultAsync(token => token.TokenHash == tokenHash, cancellationToken);

    public async Task<IReadOnlyList<RefreshToken>> ListUsableForUserAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        List<RefreshToken> tokens = await context.RefreshTokens
            .Where(token => token.UserId == userId && token.RevokedAt == null)
            .ToListAsync(cancellationToken);

        return tokens;
    }

    public Task AddAsync(RefreshToken token, CancellationToken cancellationToken) =>
        context.RefreshTokens.AddAsync(token, cancellationToken).AsTask();
}