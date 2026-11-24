using FluentValidation;

namespace BandUp.Application.Features.Learning.Admin.EditLesson;

public sealed class EditLessonValidator : AbstractValidator<EditLessonCommand>
{
    public EditLessonValidator()
    {
        RuleFor(command => command.Input.Title).NotEmpty().OverridePropertyName("title");
        RuleFor(command => command.Input.Body).NotEmpty().OverridePropertyName("body");
    }
}
