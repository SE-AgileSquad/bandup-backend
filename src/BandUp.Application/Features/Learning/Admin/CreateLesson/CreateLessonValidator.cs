using FluentValidation;

namespace BandUp.Application.Features.Learning.Admin.CreateLesson;

public sealed class CreateLessonValidator : AbstractValidator<CreateLessonCommand>
{
    public CreateLessonValidator()
    {
        RuleFor(command => command.Input.QuestionType)
            .Must(type => type.HasValue && Enum.IsDefined(type.Value))
            .WithMessage("A supported question type is required.")
            .OverridePropertyName("questionType");
        RuleFor(command => command.Input.Title).NotEmpty().OverridePropertyName("title");
        RuleFor(command => command.Input.Body).NotEmpty().OverridePropertyName("body");
    }
}
