using BandUp.Domain.Identity;

namespace BandUp.Application.Features.Identity;

/// <summary>
/// Session store for refresh tokens. Lookups are by the hash of the presented token, never by
/// the raw value.
/// </summary>
public interface IRefreshTokenStore
{
    Task<RefreshToken?> FindByTokenHashAsync(string tokenHash, CancellationToken cancellationToken);

    Task<IReadOnlyList<RefreshToken>> ListUsableForUserAsync(Guid userId, CancellationToken cancellationToken);

    Task AddAsync(RefreshToken token, CancellationToken cancellationToken);
}