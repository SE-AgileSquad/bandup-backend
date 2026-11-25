using FluentValidation;

namespace BandUp.Application.Features.Identity.Register;

/// <summary>
/// AUTH-03: every failing field is reported at once, not one at a time.
/// </summary>
public sealed class RegisterValidator : AbstractValidator<RegisterCommand>
{
    public const int MinPasswordLength = PasswordPolicy.MinimumLength;
    public const int MaxPasswordLength = PasswordPolicy.MaximumLength;
    public const int MaxEmailLength = 320;
    public const int MaxDisplayNameLength = 60;

    public RegisterValidator()
    {
        RuleFor(command => command.Email)
            .NotEmpty()
            .EmailAddress()
            .MaximumLength(MaxEmailLength);

        PasswordPolicy.Apply(RuleFor(command => command.Password));

        RuleFor(command => command.DisplayName)
            .NotEmpty()
            .MaximumLength(MaxDisplayNameLength);
    }
}
