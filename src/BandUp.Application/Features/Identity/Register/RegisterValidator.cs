using FluentValidation;

namespace BandUp.Application.Features.Identity.Register;

/// <summary>
/// AUTH-03: every failing field is reported at once, not one at a time.
/// </summary>
public sealed class RegisterValidator : AbstractValidator<RegisterCommand>
{
    public const int MinPasswordLength = 10;
    public const int MaxPasswordLength = 128;
    public const int MaxEmailLength = 320;
    public const int MaxDisplayNameLength = 60;

    public RegisterValidator()
    {
        RuleFor(command => command.Email)
            .NotEmpty()
            .EmailAddress()
            .MaximumLength(MaxEmailLength);

        RuleFor(command => command.Password)
            .NotEmpty()
            .MinimumLength(MinPasswordLength)
            .MaximumLength(MaxPasswordLength)
            .Must(ContainLetterAndDigit)
            .WithMessage("The password must contain at least one letter and one digit.");

        RuleFor(command => command.DisplayName)
            .NotEmpty()
            .MaximumLength(MaxDisplayNameLength);
    }

    private static bool ContainLetterAndDigit(string password) =>
        password.Any(char.IsLetter) && password.Any(char.IsDigit);
}
