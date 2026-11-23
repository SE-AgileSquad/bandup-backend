using BandUp.Application.Abstractions;
using BandUp.Application.Common;
using BandUp.Domain.Identity;

namespace BandUp.Application.Features.Identity.Login;

/// <summary>
/// AUTH-06 and 07. CAPTCHA runs first. Both failure paths — unknown email, wrong password —
/// return the same error and do comparable work: an unknown email still verifies a password
/// against the precomputed dummy hash, so response timing does not reveal which emails exist.
/// A deactivated account is indistinguishable from a wrong password for the same reason.
/// </summary>
public sealed class LoginHandler(
    IUserRepository userRepository,
    IPasswordHasher passwordHasher,
    ICaptchaVerifier captchaVerifier,
    ISessionTokenIssuer sessionTokenIssuer,
    IRefreshTokenStore refreshTokenStore,
    TimeProvider timeProvider) : ICommandHandler<LoginCommand, LoginResult>
{
    public async Task<Result<LoginResult>> HandleAsync(
        LoginCommand command,
        CancellationToken cancellationToken)
    {
        CaptchaOutcome captcha = await captchaVerifier.VerifyAsync(
            command.CaptchaToken,
            null,
            cancellationToken);

        if (captcha is CaptchaOutcome.InvalidToken)
        {
            return Result<LoginResult>.Failure(Error.CaptchaFailed());
        }

        if (captcha is CaptchaOutcome.ProviderUnavailable)
        {
            return Result<LoginResult>.Failure(Error.CaptchaUnavailable());
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        User? user = await userRepository.FindByEmailAsync(command.Email, cancellationToken);

        // The same two operations run on both paths: one fetch and one BCrypt verification.
        // When the user does not exist the verification runs against a hash that cannot match.
        bool verified = passwordHasher.Verify(
            command.Password,
            user?.PasswordHash ?? passwordHasher.DummyHash);

        if (user is null || !user.IsActive || !verified)
        {
            return Result<LoginResult>.Failure(Error.InvalidCredentials());
        }

        user.RecordLogin(now);

        IssuedSession session = sessionTokenIssuer.Issue(user, now, command.UserAgent);
        await refreshTokenStore.AddAsync(session.RefreshToken, cancellationToken);

        return Result<LoginResult>.Success(new LoginResult(
            user.Id,
            user.Email,
            user.DisplayName,
            user.Role.ToString(),
            session.AccessToken,
            session.RawRefreshToken,
            session.RefreshToken.ExpiresAt));
    }
}
