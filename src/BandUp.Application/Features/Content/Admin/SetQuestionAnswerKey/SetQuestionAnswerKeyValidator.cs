using FluentValidation;

namespace BandUp.Application.Features.Content.Admin.SetQuestionAnswerKey;

public sealed class SetQuestionAnswerKeyValidator : AbstractValidator<SetQuestionAnswerKeyCommand>
{
    public SetQuestionAnswerKeyValidator()
    {
        RuleFor(command => command.Input.CorrectAnswer).NotEmpty().OverridePropertyName("correctAnswer");
        RuleForEach(command => command.Input.AcceptedVariants).NotEmpty().OverridePropertyName("acceptedVariants");
    }
}
