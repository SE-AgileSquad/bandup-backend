using BandUp.Application.Abstractions;
using BandUp.Application.Common;
using BandUp.Domain.Identity;

namespace BandUp.Application.Features.Identity.Register;

/// <summary>
/// AUTH-01 to 04. CAPTCHA is verified before anything is written, so a failed check leaves
/// no account and no session (AUTH-04). The duplicate check is case-insensitive through the
/// citext column; the unique index is the race backstop — under a concurrent double submit
/// the second insert fails the index and surfaces as EMAIL_TAKEN, never as a 500.
/// </summary>
public sealed class RegisterHandler(
    IUserRepository userRepository,
    IPasswordHasher passwordHasher,
    ICaptchaVerifier captchaVerifier,
    ICreditService creditService,
    TimeProvider timeProvider) : ICommandHandler<RegisterCommand, Guid>
{
    public async Task<Result<Guid>> HandleAsync(
        RegisterCommand command,
        CancellationToken cancellationToken)
    {
        CaptchaOutcome captcha = await captchaVerifier.VerifyAsync(
            command.CaptchaToken,
            null,
            cancellationToken);

        if (captcha is CaptchaOutcome.InvalidToken)
        {
            return Result<Guid>.Failure(Error.CaptchaFailed());
        }

        if (captcha is CaptchaOutcome.ProviderUnavailable)
        {
            return Result<Guid>.Failure(Error.CaptchaUnavailable());
        }

        if (await userRepository.EmailExistsAsync(command.Email, cancellationToken))
        {
            return Result<Guid>.Failure(Error.EmailTaken());
        }

        User user = User.Create(
            command.Email,
            command.DisplayName,
            passwordHasher.Hash(command.Password),
            timeProvider.GetUtcNow());

        await userRepository.AddAsync(user, cancellationToken);

        // Register's transaction commits both rows or neither: a user without a credit
        // account, or an account without its user, is never observable.
        Result account = await creditService.OpenSignupAccountAsync(user.Id, cancellationToken);

        if (account.IsFailure)
        {
            return Result<Guid>.Failure(account.Error!);
        }

        return Result<Guid>.Success(user.Id);
    }
}
