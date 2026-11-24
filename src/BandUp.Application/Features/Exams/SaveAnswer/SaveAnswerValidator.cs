using BandUp.Domain.Exams;
using FluentValidation;

namespace BandUp.Application.Features.Exams.SaveAnswer;

public sealed class SaveAnswerValidator : AbstractValidator<SaveAnswerCommand>
{
    public SaveAnswerValidator()
    {
        RuleFor(command => command.AttemptId).NotEmpty();
        RuleFor(command => command.QuestionId).NotEmpty();
        RuleFor(command => command.SelectedAnswer).MaximumLength(Attempt.MaxAnswerLength);
    }
}
