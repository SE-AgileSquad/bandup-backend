using BandUp.Application.Features.Identity.Register;
using FluentValidation;

namespace BandUp.Application.Features.Identity.ResetPassword;

public sealed class ResetPasswordValidator : AbstractValidator<ResetPasswordCommand>
{
    public ResetPasswordValidator()
    {
        RuleFor(command => command.Email)
            .NotEmpty()
            .EmailAddress()
            .MaximumLength(RegisterValidator.MaxEmailLength);

        RuleFor(command => command.Code)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .Must(code => code is not null &&
                code.Length == PasswordResetPolicy.CodeLength && code.All(char.IsAsciiDigit))
            .WithMessage("The code must contain exactly six digits (0-9).");

        PasswordPolicy.Apply(RuleFor(command => command.NewPassword));
    }
}
