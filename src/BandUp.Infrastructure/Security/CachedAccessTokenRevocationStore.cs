using BandUp.Application.Abstractions;
using BandUp.Application.Features.Identity;
using BandUp.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace BandUp.Infrastructure.Security;

public sealed class CachedAccessTokenRevocationStore(
    ICacheService cache,
    TimeProvider timeProvider,
    ILogger<CachedAccessTokenRevocationStore> logger) : IAccessTokenRevocationStore
{
    public async Task RevokeAsync(Guid tokenId, DateTimeOffset expiresAt, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        TimeSpan remaining = expiresAt.Add(AuthSettings.AccessTokenClockSkew) - timeProvider.GetUtcNow();

        if (remaining <= TimeSpan.Zero)
        {
            return;
        }

        await cache.SetAsync(Key(tokenId), true, remaining, cancellationToken);
    }

    public async Task<bool> IsRevokedAsync(Guid tokenId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            return await cache.GetAsync<bool>(Key(tokenId), cancellationToken);
        }
        catch (RedisException exception)
        {
            logger.LogWarning(exception, "Access-token revocation lookup unavailable; permitting otherwise-valid authentication");
            return false;
        }
    }

    private static string Key(Guid tokenId) => $"auth:revoked-jti:{tokenId:N}";
}
