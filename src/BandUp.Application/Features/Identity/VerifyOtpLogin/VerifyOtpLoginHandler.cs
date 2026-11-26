using BandUp.Application.Abstractions;
using BandUp.Application.Common;
using BandUp.Application.Features.Identity.Login;
using BandUp.Domain.Identity;

namespace BandUp.Application.Features.Identity.VerifyOtpLogin;

public sealed class VerifyOtpLoginHandler(
    IOtpLoginRateLimiter rateLimiter,
    IOtpLoginChallengeStore challenges,
    IOtpLoginCodeProtector codes,
    ISessionTokenIssuer sessionTokenIssuer,
    IRefreshTokenStore refreshTokens,
    TimeProvider timeProvider) : ICommandHandler<VerifyOtpLoginCommand, VerifyOtpLoginOutcome>
{
    public async Task<Result<VerifyOtpLoginOutcome>> HandleAsync(VerifyOtpLoginCommand command, CancellationToken cancellationToken)
    {
        OtpLoginRateLimitDecision decision = await rateLimiter.AcquireVerificationAsync(command.Email, command.ClientIp, cancellationToken);
        switch (decision)
        {
            case OtpLoginRateLimitDecision.ClientRateLimited:
                return Result<VerifyOtpLoginOutcome>.Failure(Error.OtpLoginRateLimited());
            case OtpLoginRateLimitDecision.AccountSuppressed:
                return Rejected();
            case OtpLoginRateLimitDecision.Allowed:
                break;
            default:
                throw new InvalidOperationException("Unexpected OTP sign-in quota decision.");
        }

        OtpLoginSession? session = await challenges.FindForUpdateAsync(command.Email, cancellationToken);
        OtpLoginChallenge? challenge = session?.Challenge;
        DateTimeOffset now = timeProvider.GetUtcNow();
        // Do MAC work even when there is no usable account or challenge.
        bool verified = codes.Verify(challenge?.Id ?? Guid.CreateVersion7(), session?.User.Id ?? Guid.CreateVersion7(),
            command.Code, challenge?.CodeMac ?? new string('0', 64));
        if (session is null || !session.User.IsActive || challenge is null || !challenge.IsUsable(now))
        {
            return Rejected();
        }
        now = timeProvider.GetUtcNow();
        if (!challenge.IsUsable(now))
        {
            return Rejected();
        }
        if (!verified)
        {
            challenge.RecordFailedAttempt(now);
            return Rejected();
        }

        IssuedSession issued = sessionTokenIssuer.Issue(session.User, now, command.UserAgent);
        now = timeProvider.GetUtcNow();
        if (!challenge.IsUsable(now))
        {
            return Rejected();
        }
        cancellationToken.ThrowIfCancellationRequested();
        challenge.Consume(now);
        session.User.RecordLogin(now);
        await challenges.InvalidateOthersAsync(session.User.Id, challenge.Id, now, cancellationToken);
        await refreshTokens.AddAsync(issued.RefreshToken, cancellationToken);
        return Result<VerifyOtpLoginOutcome>.Success(new VerifyOtpLoginOutcome.Completed(new LoginResult(
            session.User.Id, session.User.Email, session.User.DisplayName, session.User.Role.ToString(),
            issued.AccessToken, issued.RawRefreshToken, issued.RefreshToken.ExpiresAt)));
    }

    // Success carries the rejection so TransactionBehavior commits incorrect-guess counters.
    private static Result<VerifyOtpLoginOutcome> Rejected() =>
        Result<VerifyOtpLoginOutcome>.Success(new VerifyOtpLoginOutcome.Rejected());
}
