using FluentValidation;

namespace BandUp.Application.Features.Content.Admin.ListAdminPassages;

public sealed class ListAdminPassagesValidator : AbstractValidator<ListAdminPassagesQuery>
{
    public const int MaxSearchLength = 320;

    public ListAdminPassagesValidator()
    {
        RuleFor(query => query.Status).Must(status => !status.HasValue || Enum.IsDefined(status.Value))
            .WithMessage("A supported passage status is required.").OverridePropertyName("status");
        RuleFor(query => query.Search).MaximumLength(MaxSearchLength).OverridePropertyName("search");
    }
}
