using FluentValidation;

namespace BandUp.Application.Features.Content.Admin.SetQuestionExplanation;

public sealed class SetQuestionExplanationValidator : AbstractValidator<SetQuestionExplanationCommand>
{
    public SetQuestionExplanationValidator()
    {
        RuleFor(command => command.Input.Explanation).NotEmpty().OverridePropertyName("explanation");
    }
}
