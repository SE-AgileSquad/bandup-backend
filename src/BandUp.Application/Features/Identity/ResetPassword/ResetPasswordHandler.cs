using BandUp.Application.Abstractions;
using BandUp.Application.Common;
using BandUp.Domain.Identity;

namespace BandUp.Application.Features.Identity.ResetPassword;

public sealed class ResetPasswordHandler(
    IPasswordResetRateLimiter rateLimiter,
    IPasswordResetChallengeStore challenges,
    IPasswordResetTokenProtector tokenProtector,
    IPasswordResetTokenStore resetTokens,
    IPasswordHasher passwordHasher,
    IRefreshTokenStore refreshTokens,
    IOtpLoginChallengeStore otpLoginChallenges,
    IPostCommitEmailQueue emailQueue,
    TimeProvider timeProvider) : ICommandHandler<ResetPasswordCommand, ResetPasswordOutcome>
{
    public async Task<Result<ResetPasswordOutcome>> HandleAsync(ResetPasswordCommand command, CancellationToken cancellationToken)
    {
        string tokenHash = tokenProtector.HashToken(command.ResetToken);
        PasswordResetRateLimitDecision decision = await rateLimiter.AcquireResetAsync(tokenHash, command.ClientIp, cancellationToken);
        if (decision == PasswordResetRateLimitDecision.ClientRateLimited)
        {
            return Result<ResetPasswordOutcome>.Failure(Error.PasswordResetRateLimited());
        }
        if (decision == PasswordResetRateLimitDecision.AccountSuppressed)
        {
            return Rejected();
        }
        if (decision != PasswordResetRateLimitDecision.Allowed)
        {
            throw new InvalidOperationException("Unexpected recovery quota decision.");
        }

        PasswordResetTokenSession? session = await resetTokens.FindForUpdateAsync(tokenHash, cancellationToken);
        DateTimeOffset now = timeProvider.GetUtcNow();
        if (session is null || !session.User.IsActive || !session.Token.IsUsable(now))
        {
            return Rejected();
        }

        string newHash = passwordHasher.Hash(command.NewPassword);
        cancellationToken.ThrowIfCancellationRequested();
        now = timeProvider.GetUtcNow();
        // Hashing can outlast the authorization's remaining validity.
        if (!session.Token.IsUsable(now))
        {
            return Rejected();
        }

        session.User.ChangePassword(newHash);
        session.Token.Consume(now);
        await resetTokens.InvalidateOthersAsync(session.User.Id, session.Token.Id, now, cancellationToken);
        await challenges.InvalidateAllForUserAsync(session.User.Id, now, cancellationToken);
        await refreshTokens.RevokeAllForUserAsync(session.User.Id, now, cancellationToken);
        await otpLoginChallenges.InvalidateAllForUserAsync(session.User.Id, now, cancellationToken);
        emailQueue.Enqueue(new EmailMessage(session.User.Email, "Your BandUp password has changed",
            "Your BandUp password has been changed.\n\nIf you did not make this change, contact support."));
        return Result<ResetPasswordOutcome>.Success(new ResetPasswordOutcome.Completed());
    }

    // The endpoint maps the outcome only after the command transaction completes.
    private static Result<ResetPasswordOutcome> Rejected() =>
        Result<ResetPasswordOutcome>.Success(new ResetPasswordOutcome.Rejected());
}
