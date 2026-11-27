using FluentValidation;

namespace BandUp.Application.Features.Writing.Admin.EditWritingPrompt;

public sealed class EditWritingPromptValidator : AbstractValidator<EditWritingPromptCommand>
{
    public EditWritingPromptValidator()
    {
        RuleFor(command => command.Input.DurationMinutes).Must(minutes => !minutes.HasValue || minutes.Value > 0)
            .WithMessage("Duration must be a positive number of minutes.").OverridePropertyName("durationMinutes");
        RuleFor(command => command.Input.Title).NotEmpty().MaximumLength(320).OverridePropertyName("title");
        RuleFor(command => command.Input.TaskText).NotEmpty().OverridePropertyName("taskText");
        RuleFor(command => command.Input.EssayType).NotEmpty().MaximumLength(100).OverridePropertyName("essayType");
        RuleFor(command => command.Input.MinWords).GreaterThan(0).OverridePropertyName("minWords");
        RuleFor(command => command.Input.MaxWords).GreaterThanOrEqualTo(command => command.Input.MinWords)
            .OverridePropertyName("maxWords");
    }
}

