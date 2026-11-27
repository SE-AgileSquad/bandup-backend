using BandUp.Application.Features.Identity.Register;
using FluentValidation;

namespace BandUp.Application.Features.Identity.VerifyOtpLogin;

public sealed class VerifyOtpLoginValidator : AbstractValidator<VerifyOtpLoginCommand>
{
    public VerifyOtpLoginValidator()
    {
        RuleFor(command => command.Email)
            .NotEmpty()
            .EmailAddress()
            .MaximumLength(RegisterValidator.MaxEmailLength);

        RuleFor(command => command.Code)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .Must(code => code is not null &&
                code.Length == OtpLoginPolicy.CodeLength && code.All(char.IsAsciiDigit))
            .WithMessage("The code must contain exactly six digits (0-9).");
    }
}
