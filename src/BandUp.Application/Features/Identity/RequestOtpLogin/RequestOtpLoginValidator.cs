using BandUp.Application.Features.Identity.Register;
using FluentValidation;

namespace BandUp.Application.Features.Identity.RequestOtpLogin;

public sealed class RequestOtpLoginValidator : AbstractValidator<RequestOtpLoginCommand>
{
    public RequestOtpLoginValidator()
    {
        RuleFor(command => command.Email)
            .NotEmpty()
            .EmailAddress()
            .MaximumLength(RegisterValidator.MaxEmailLength);

        RuleFor(command => command.CaptchaToken)
            .NotEmpty()
            .MaximumLength(OtpLoginPolicy.MaxCaptchaTokenLength);
    }
}
