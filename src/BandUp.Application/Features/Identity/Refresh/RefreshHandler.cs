using BandUp.Application.Abstractions;
using BandUp.Application.Common;
using Microsoft.Extensions.Logging;

namespace BandUp.Application.Features.Identity.Refresh;

public sealed class RefreshHandler(
    ITokenService tokens,
    IRefreshTokenStore refreshTokens,
    ISessionTokenIssuer sessionTokenIssuer,
    TimeProvider timeProvider,
    ILogger<RefreshHandler> logger) : ICommandHandler<RefreshCommand, RefreshOutcome>
{
    public async Task<Result<RefreshOutcome>> HandleAsync(
        RefreshCommand command,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(command.RefreshToken))
        {
            return Result<RefreshOutcome>.Failure(Error.InvalidRefreshToken());
        }

        string tokenHash = tokens.HashRefreshToken(command.RefreshToken);
        RefreshSession? session = await refreshTokens.FindSessionForUpdateAsync(tokenHash, cancellationToken);

        if (session is null)
        {
            return Result<RefreshOutcome>.Failure(Error.InvalidRefreshToken());
        }

        // A lock wait can outlast a token's remaining lifetime.
        DateTimeOffset now = timeProvider.GetUtcNow();

        // Reuse remains a security event even when the revoked token has since expired or
        // its owner was deactivated. The completed outcome lets the pipeline commit revocation.
        if (session.Token.RevokedAt is not null)
        {
            await refreshTokens.RevokeAllForUserAsync(session.User.Id, now, cancellationToken);
            logger.LogWarning(
                "Refresh-token reuse detected for {UserId}; revoking remaining refresh sessions",
                session.User.Id);

            return Result<RefreshOutcome>.Success(new RefreshOutcome.RejectedAfterRevocation());
        }

        if (!session.User.IsActive || session.Token.IsExpired(now))
        {
            return Result<RefreshOutcome>.Failure(Error.InvalidRefreshToken());
        }

        IssuedSession issued = sessionTokenIssuer.Issue(session.User, now, command.UserAgent);
        session.Token.Rotate(issued.RefreshToken);
        await refreshTokens.AddAsync(issued.RefreshToken, cancellationToken);

        return Result<RefreshOutcome>.Success(new RefreshOutcome.Issued(
            issued.AccessToken,
            issued.RawRefreshToken,
            issued.RefreshToken.ExpiresAt));
    }
}
