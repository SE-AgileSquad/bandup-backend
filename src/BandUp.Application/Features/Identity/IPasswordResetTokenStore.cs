using BandUp.Domain.Identity;

namespace BandUp.Application.Features.Identity;

/// <summary>Requires the caller's Read Committed transaction; locks the user before token state.</summary>
public interface IPasswordResetTokenStore
{
    Task<PasswordResetTokenSession?> FindForUpdateAsync(string tokenHash, CancellationToken cancellationToken);
    Task ReplaceAsync(PasswordResetToken token, CancellationToken cancellationToken);
    Task InvalidateAllForUserAsync(Guid userId, DateTimeOffset at, CancellationToken cancellationToken);
    Task InvalidateOthersAsync(Guid userId, Guid retainedTokenId, DateTimeOffset at, CancellationToken cancellationToken);
}
