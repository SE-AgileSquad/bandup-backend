using FluentValidation;

namespace BandUp.Application.Features.Identity.Admin.ChangeUserRole;

public sealed class ChangeUserRoleValidator : AbstractValidator<ChangeUserRoleCommand>
{
    public ChangeUserRoleValidator()
    {
        RuleFor(command => command.Role)
            .Must(role => role.HasValue && Enum.IsDefined(role.Value))
            .WithMessage("A supported role is required.")
            .OverridePropertyName("role");
    }
}
