using BandUp.Application.Features.Identity.Register;
using FluentValidation;

namespace BandUp.Application.Features.Identity.Admin.CreateAdminUser;

public sealed class CreateAdminUserValidator : AbstractValidator<CreateAdminUserCommand>
{
    public CreateAdminUserValidator()
    {
        RuleFor(command => command.Email).NotEmpty().EmailAddress()
            .MaximumLength(RegisterValidator.MaxEmailLength).OverridePropertyName("email");
        RuleFor(command => command.DisplayName).NotEmpty()
            .MaximumLength(RegisterValidator.MaxDisplayNameLength).OverridePropertyName("displayName");
        RuleFor(command => command.Password).NotEmpty()
            .MinimumLength(RegisterValidator.MinPasswordLength)
            .MaximumLength(RegisterValidator.MaxPasswordLength)
            .Must(password => !string.IsNullOrEmpty(password) && password.Any(char.IsLetter) && password.Any(char.IsDigit))
            .WithMessage("The password must contain at least one letter and one digit.")
            .OverridePropertyName("password");
        RuleFor(command => command.Role).Must(role => role.HasValue && Enum.IsDefined(role.Value))
            .WithMessage("A supported role is required.").OverridePropertyName("role");
    }
}
