using BandUp.Application.Abstractions;
using BandUp.Application.Common;
using BandUp.Domain.Identity;

namespace BandUp.Application.Features.Identity.VerifyResetCode;

public sealed class VerifyResetCodeHandler(
    IPasswordResetRateLimiter rateLimiter,
    IPasswordResetChallengeStore challenges,
    IPasswordResetCodeProtector codes,
    IPasswordResetTokenProtector tokenProtector,
    IPasswordResetTokenStore resetTokens,
    TimeProvider timeProvider) : ICommandHandler<VerifyResetCodeCommand, VerifyResetCodeOutcome>
{
    public async Task<Result<VerifyResetCodeOutcome>> HandleAsync(VerifyResetCodeCommand command, CancellationToken cancellationToken)
    {
        PasswordResetRateLimitDecision decision = await rateLimiter.AcquireVerificationAsync(command.Email, command.ClientIp, cancellationToken);
        switch (decision)
        {
            case PasswordResetRateLimitDecision.ClientRateLimited:
                return Result<VerifyResetCodeOutcome>.Failure(Error.PasswordResetRateLimited());
            case PasswordResetRateLimitDecision.AccountSuppressed:
                return Rejected();
            case PasswordResetRateLimitDecision.Allowed:
                break;
            default:
                throw new InvalidOperationException("Unexpected recovery quota decision.");
        }
        PasswordResetSession? session = await challenges.FindForUpdateAsync(command.Email, cancellationToken);
        PasswordResetChallenge? challenge = session?.Challenge;
        // Match the cryptographic work for missing, closed and eligible challenges.
        bool verified = codes.Verify(challenge?.Id ?? Guid.CreateVersion7(), session?.User.Id ?? Guid.CreateVersion7(),
            command.Code, challenge?.CodeMac ?? new string('0', 64));
        DateTimeOffset now = timeProvider.GetUtcNow();
        if (session is null || !session.User.IsActive || challenge is null || !challenge.IsUsable(now)) { return Rejected(); }
        if (!verified)
        {
            challenge.RecordFailedAttempt(now);
            return Rejected();
        }
        string raw = tokenProtector.GenerateToken();
        string hash = tokenProtector.HashToken(raw);
        now = timeProvider.GetUtcNow();
        if (!challenge.IsUsable(now)) { return Rejected(); }
        cancellationToken.ThrowIfCancellationRequested();
        challenge.Consume(now);
        await challenges.InvalidateOthersAsync(session.User.Id, challenge.Id, now, cancellationToken);
        PasswordResetToken token = PasswordResetToken.Issue(challenge, hash, now, now.Add(PasswordResetPolicy.ResetTokenLifetime));
        await resetTokens.ReplaceAsync(token, cancellationToken);
        return Result<VerifyResetCodeOutcome>.Success(new VerifyResetCodeOutcome.Verified(raw, token.ExpiresAt));
    }

    private static Result<VerifyResetCodeOutcome> Rejected() => Result<VerifyResetCodeOutcome>.Success(new VerifyResetCodeOutcome.Rejected());
}
