using FluentValidation;

namespace BandUp.Application.Features.Writing.Admin.ListAdminWritingPrompts;

public sealed class ListAdminWritingPromptsValidator : AbstractValidator<ListAdminWritingPromptsQuery>
{
    public ListAdminWritingPromptsValidator()
    {
        RuleFor(query => query.Status).Must(status => !status.HasValue || Enum.IsDefined(status.Value))
            .WithMessage("A supported writing prompt status is required.").OverridePropertyName("status");
        RuleFor(query => query.Search).MaximumLength(320).OverridePropertyName("search");
    }
}

