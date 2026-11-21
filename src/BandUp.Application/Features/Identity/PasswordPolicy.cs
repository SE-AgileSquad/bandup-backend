using FluentValidation;

namespace BandUp.Application.Features.Identity;

/// <summary>One password policy for registration and password recovery.</summary>
public static class PasswordPolicy
{
    public const int MinimumLength = 10;
    public const int MaximumLength = 128;

    internal static IRuleBuilderOptions<T, string> Apply<T>(IRuleBuilder<T, string> rule) =>
        rule.NotEmpty()
            .MinimumLength(MinimumLength)
            .MaximumLength(MaximumLength)
            .Must(password => password is not null && password.Any(char.IsLetter) && password.Any(char.IsDigit))
            .WithMessage("The password must contain at least one letter and one digit.");
}
