using FluentValidation;

namespace BandUp.Application.Features.Identity.Login;

/// <summary>
/// Structural checks only: a wrong password is an expected failure, not a validation error,
/// so the validator never looks at whether the credential is correct.
/// </summary>
public sealed class LoginValidator : AbstractValidator<LoginCommand>
{
    public LoginValidator()
    {
        RuleFor(command => command.Email)
            .NotEmpty()
            .EmailAddress();

        RuleFor(command => command.Password)
            .NotEmpty();
    }
}
