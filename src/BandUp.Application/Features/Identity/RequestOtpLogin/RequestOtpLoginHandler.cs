using BandUp.Application.Abstractions;
using BandUp.Application.Common;
using BandUp.Domain.Identity;

namespace BandUp.Application.Features.Identity.RequestOtpLogin;

public sealed class RequestOtpLoginHandler(
    ICaptchaVerifier captchaVerifier,
    IOtpLoginRateLimiter rateLimiter,
    IOtpLoginChallengeStore challenges,
    IOtpLoginCodeProtector codes,
    IPostCommitEmailQueue emailQueue,
    TimeProvider timeProvider) : ICommandHandler<RequestOtpLoginCommand, RequestOtpLoginOutcome>
{
    public async Task<Result<RequestOtpLoginOutcome>> HandleAsync(RequestOtpLoginCommand command, CancellationToken cancellationToken)
    {
        CaptchaOutcome captcha = await captchaVerifier.VerifyAsync(command.CaptchaToken!, command.ClientIp, cancellationToken);
        if (captcha is CaptchaOutcome.InvalidToken)
        {
            return Result<RequestOtpLoginOutcome>.Failure(Error.CaptchaFailed());
        }
        if (captcha is CaptchaOutcome.ProviderUnavailable)
        {
            return Result<RequestOtpLoginOutcome>.Failure(Error.CaptchaUnavailable());
        }

        OtpLoginRateLimitDecision decision = await rateLimiter.AcquireRequestAsync(command.Email, command.ClientIp, cancellationToken);
        switch (decision)
        {
            case OtpLoginRateLimitDecision.ClientRateLimited:
                return Result<RequestOtpLoginOutcome>.Failure(Error.OtpLoginRateLimited());
            case OtpLoginRateLimitDecision.AccountSuppressed:
                return Accepted();
            case OtpLoginRateLimitDecision.Allowed:
                break;
            default:
                throw new InvalidOperationException("Unexpected OTP sign-in quota decision.");
        }

        OtpLoginSession? session = await challenges.FindForUpdateAsync(command.Email, cancellationToken);
        DateTimeOffset now = timeProvider.GetUtcNow();
        Guid id = Guid.CreateVersion7();
        string code = codes.GenerateCode();
        // Unknown and inactive accounts still perform code generation and protection.
        string mac = codes.Protect(id, session?.User.Id ?? Guid.CreateVersion7(), code);
        if (session is null || !session.User.IsActive ||
            session.Challenge is not null && now < session.Challenge.CreatedAt.Add(OtpLoginPolicy.ResendCooldown))
        {
            return Accepted();
        }

        OtpLoginChallenge challenge = OtpLoginChallenge.Issue(
            id, session.User.Id, mac, now, now.Add(OtpLoginPolicy.CodeLifetime), OtpLoginPolicy.MaxFailedAttempts);
        await challenges.ReplaceAsync(challenge, cancellationToken);
        emailQueue.Enqueue(new EmailMessage(session.User.Email, "Your BandUp sign-in code",
            $"Your sign-in code is {code}.\n\nThis code expires in 10 minutes. Never share this code.\nIf you did not request a sign-in code, you can ignore this email."), challenge.ExpiresAt);
        return Accepted();
    }

    private static Result<RequestOtpLoginOutcome> Accepted() => Result<RequestOtpLoginOutcome>.Success(new RequestOtpLoginOutcome());
}
