using FluentValidation;

namespace BandUp.Application.Features.Identity.ResetPassword;

public sealed class ResetPasswordValidator : AbstractValidator<ResetPasswordCommand>
{
    public ResetPasswordValidator()
    {
        RuleFor(command => command.ResetToken)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .Must(PasswordResetPolicy.IsResetToken)
            .WithMessage("A valid password-reset token is required.");

        PasswordPolicy.Apply(RuleFor(command => command.NewPassword));
    }
}
