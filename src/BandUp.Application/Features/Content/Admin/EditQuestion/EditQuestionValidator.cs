using FluentValidation;

namespace BandUp.Application.Features.Content.Admin.EditQuestion;

public sealed class EditQuestionValidator : AbstractValidator<EditQuestionCommand>
{
    public EditQuestionValidator()
    {
        RuleFor(command => command.Input.Prompt).NotEmpty().OverridePropertyName("prompt");
    }
}
