using BandUp.Application.Abstractions;
using BandUp.Application.Common;
using BandUp.Domain.Identity;

namespace BandUp.Application.Features.Identity.ForgotPassword;

public sealed class ForgotPasswordHandler(
    IPasswordResetRateLimiter rateLimiter,
    IPasswordResetChallengeStore challenges,
    IPasswordResetCodeProtector codes,
    IPasswordResetTokenStore resetTokens,
    IPostCommitEmailQueue emailQueue,
    TimeProvider timeProvider) : ICommandHandler<ForgotPasswordCommand, ForgotPasswordOutcome>
{
    public async Task<Result<ForgotPasswordOutcome>> HandleAsync(ForgotPasswordCommand command, CancellationToken cancellationToken)
    {
        PasswordResetRateLimitDecision decision = await rateLimiter.AcquireRequestAsync(command.Email, command.ClientIp, cancellationToken);
        if (decision == PasswordResetRateLimitDecision.ClientRateLimited)
        {
            return Result<ForgotPasswordOutcome>.Failure(Error.PasswordResetRateLimited());
        }
        if (decision == PasswordResetRateLimitDecision.AccountSuppressed)
        {
            return Accepted();
        }
        if (decision != PasswordResetRateLimitDecision.Allowed)
        {
            throw new InvalidOperationException("Unexpected recovery quota decision.");
        }

        PasswordResetSession? session = await challenges.FindForUpdateAsync(command.Email, cancellationToken);
        DateTimeOffset now = timeProvider.GetUtcNow();
        Guid id = Guid.CreateVersion7();
        string code = codes.GenerateCode();
        // Perform the same cryptographic work for an unknown or inactive account.
        string mac = codes.Protect(id, session?.User.Id ?? Guid.CreateVersion7(), code);
        if (session is null || !session.User.IsActive ||
            session.Challenge is not null && now < session.Challenge.CreatedAt.Add(PasswordResetPolicy.ResendCooldown))
        {
            return Accepted();
        }

        PasswordResetChallenge challenge = PasswordResetChallenge.Issue(
            id, session.User.Id, mac, now, now.Add(PasswordResetPolicy.CodeLifetime), PasswordResetPolicy.MaxFailedAttempts);
        await challenges.ReplaceAsync(challenge, cancellationToken);
        await resetTokens.InvalidateAllForUserAsync(session.User.Id, now, cancellationToken);
        emailQueue.Enqueue(new EmailMessage(session.User.Email, "Your BandUp password-reset code",
            $"Your password-reset code is {code}.\n\nThis code expires in 10 minutes.\nIf you did not request a password reset, you can ignore this email."),
            challenge.ExpiresAt);
        return Accepted();
    }

    private static Result<ForgotPasswordOutcome> Accepted() => Result<ForgotPasswordOutcome>.Success(new ForgotPasswordOutcome());
}
