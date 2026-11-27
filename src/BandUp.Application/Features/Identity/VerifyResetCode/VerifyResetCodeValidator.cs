using BandUp.Application.Features.Identity.Register;
using FluentValidation;

namespace BandUp.Application.Features.Identity.VerifyResetCode;

public sealed class VerifyResetCodeValidator : AbstractValidator<VerifyResetCodeCommand>
{
    public VerifyResetCodeValidator()
    {
        RuleFor(command => command.Email).NotEmpty().EmailAddress().MaximumLength(RegisterValidator.MaxEmailLength);
        RuleFor(command => command.Code).Cascade(CascadeMode.Stop).NotEmpty()
            .Must(code => code is { Length: PasswordResetPolicy.CodeLength } && code.All(char.IsAsciiDigit))
            .WithMessage("The code must contain exactly six digits (0-9).");
    }
}
