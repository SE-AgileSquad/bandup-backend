using BandUp.Application.Abstractions;
using BandUp.Application.Common;

namespace BandUp.Application.Features.Identity.Logout;

public sealed class LogoutHandler(
    ICurrentUser currentUser,
    ITokenService tokens,
    IRefreshTokenStore refreshTokens,
    IAccessTokenRevocationStore accessRevocations,
    TimeProvider timeProvider) : ICommandHandler<LogoutCommand, LogoutOutcome>
{
    public async Task<Result<LogoutOutcome>> HandleAsync(LogoutCommand command, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        AccessTokenIdentity? identity = currentUser.AccessToken;

        if (!currentUser.IsAuthenticated || identity is null || identity.UserId == Guid.Empty || identity.TokenId == Guid.Empty)
        {
            return Result<LogoutOutcome>.Failure(Error.Unauthorized("Sign in to access this resource."));
        }

        if (!string.IsNullOrWhiteSpace(command.RefreshToken))
        {
            await refreshTokens.RevokeForUserAsync(
                identity.UserId,
                tokens.HashRefreshToken(command.RefreshToken),
                timeProvider.GetUtcNow(),
                cancellationToken);
        }

        // Redis is not enlisted in the database transaction. A failed write must abort the
        // command; a later database failure can leave this JWT blacklisted (ADR 0016).
        await accessRevocations.RevokeAsync(identity.TokenId, identity.ExpiresAt, cancellationToken);

        return Result<LogoutOutcome>.Success(new LogoutOutcome());
    }
}
