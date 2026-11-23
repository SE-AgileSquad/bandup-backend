using FluentValidation;

namespace BandUp.Application.Features.Learning.Admin.ListAdminLessons;

public sealed class ListAdminLessonsValidator : AbstractValidator<ListAdminLessonsQuery>
{
    public const int MaxSearchLength = 320;

    public ListAdminLessonsValidator()
    {
        RuleFor(query => query.Status).Must(status => !status.HasValue || Enum.IsDefined(status.Value))
            .WithMessage("A supported lesson status is required.").OverridePropertyName("status");
        RuleFor(query => query.Search).MaximumLength(MaxSearchLength).OverridePropertyName("search");
    }
}
