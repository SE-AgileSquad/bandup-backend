using BandUp.Application.Features.Identity.Register;
using FluentValidation;

namespace BandUp.Application.Features.Identity.ForgotPassword;

public sealed class ForgotPasswordValidator : AbstractValidator<ForgotPasswordCommand>
{
    public ForgotPasswordValidator() =>
        RuleFor(command => command.Email)
            .NotEmpty()
            .EmailAddress()
            .MaximumLength(RegisterValidator.MaxEmailLength);
}
