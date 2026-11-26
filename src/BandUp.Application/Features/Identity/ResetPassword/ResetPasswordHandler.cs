using BandUp.Application.Abstractions;
using BandUp.Application.Common;
using BandUp.Domain.Identity;

namespace BandUp.Application.Features.Identity.ResetPassword;

public sealed class ResetPasswordHandler(
    IPasswordResetRateLimiter rateLimiter,
    IPasswordResetChallengeStore challenges,
    IPasswordResetCodeProtector codes,
    IPasswordHasher passwordHasher,
    IRefreshTokenStore refreshTokens,
    IOtpLoginChallengeStore otpLoginChallenges,
    IPostCommitEmailQueue emailQueue,
    TimeProvider timeProvider) : ICommandHandler<ResetPasswordCommand, ResetPasswordOutcome>
{
    public async Task<Result<ResetPasswordOutcome>> HandleAsync(ResetPasswordCommand command, CancellationToken cancellationToken)
    {
        PasswordResetRateLimitDecision decision = await rateLimiter.AcquireVerificationAsync(command.Email, command.ClientIp, cancellationToken);
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

        PasswordResetSession? session = await challenges.FindForUpdateAsync(command.Email, cancellationToken);
        DateTimeOffset now = timeProvider.GetUtcNow();
        PasswordResetChallenge? challenge = session?.Challenge;
        if (session is null || !session.User.IsActive || challenge is null || !challenge.IsUsable(now))
        {
            return Rejected();
        }

        if (!codes.Verify(challenge.Id, session.User.Id, command.Code, challenge.CodeMac))
        {
            now = timeProvider.GetUtcNow();
            if (challenge.IsUsable(now))
            {
                challenge.RecordFailedAttempt(now);
            }
            return Rejected();
        }

        string newHash = passwordHasher.Hash(command.NewPassword);
        cancellationToken.ThrowIfCancellationRequested();
        now = timeProvider.GetUtcNow();
        // Password hashing can outlast a code's remaining validity. Check before staging any writes.
        if (!challenge.IsUsable(now))
        {
            return Rejected();
        }

        session.User.ChangePassword(newHash);
        challenge.Consume(now);
        await challenges.InvalidateOthersAsync(session.User.Id, challenge.Id, now, cancellationToken);
        await refreshTokens.RevokeAllForUserAsync(session.User.Id, now, cancellationToken);
        await otpLoginChallenges.InvalidateAllForUserAsync(session.User.Id, now, cancellationToken);
        emailQueue.Enqueue(new EmailMessage(session.User.Email, "Your BandUp password has changed",
            "Your BandUp password has been changed.\n\nIf you did not make this change, contact support."));
        return Result<ResetPasswordOutcome>.Success(new ResetPasswordOutcome.Completed());
    }

    // A completed rejection commits incorrect-guess counters before the API returns its generic error.
    private static Result<ResetPasswordOutcome> Rejected() =>
        Result<ResetPasswordOutcome>.Success(new ResetPasswordOutcome.Rejected());
}
