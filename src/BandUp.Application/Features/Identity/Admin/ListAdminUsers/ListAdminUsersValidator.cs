using BandUp.Application.Features.Identity.Register;
using FluentValidation;

namespace BandUp.Application.Features.Identity.Admin.ListAdminUsers;

public sealed class ListAdminUsersValidator : AbstractValidator<ListAdminUsersQuery>
{
    public ListAdminUsersValidator()
    {
        RuleFor(query => query.Role).Must(role => !role.HasValue || Enum.IsDefined(role.Value))
            .OverridePropertyName("role");
        RuleFor(query => query.Search).MaximumLength(RegisterValidator.MaxEmailLength)
            .OverridePropertyName("search");
    }
}
