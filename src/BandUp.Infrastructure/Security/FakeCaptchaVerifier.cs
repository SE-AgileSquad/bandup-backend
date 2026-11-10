using BandUp.Application.Abstractions;

namespace BandUp.Infrastructure.Security;

/// <summary>
/// Offline verifier used by tests and local runs that select
/// <c>Captcha__Provider=Fake</c>. The sentinel token keeps the real adapter contract: an empty
/// token fails, the sentinel passes.
/// </summary>
public sealed class FakeCaptchaVerifier : ICaptchaVerifier
{
    public const string ValidToken = "fake-captcha-token";

    public Task<CaptchaOutcome> VerifyAsync(
        string? token,
        string? clientIp,
        CancellationToken cancellationToken)
    {
        CaptchaOutcome outcome = token == ValidToken
            ? CaptchaOutcome.Valid
            : CaptchaOutcome.InvalidToken;

        return Task.FromResult(outcome);
    }
}
