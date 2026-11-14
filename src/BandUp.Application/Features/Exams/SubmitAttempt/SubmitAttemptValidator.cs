using BandUp.Domain.Exams;
using FluentValidation;

namespace BandUp.Application.Features.Exams.SubmitAttempt;

public sealed class SubmitAttemptValidator : AbstractValidator<SubmitAttemptCommand>
{
    public const int MaxIdempotencyKeyLength = 100;

    public SubmitAttemptValidator()
    {
        RuleFor(command => command.AttemptId).NotEmpty();
        RuleFor(command => command.IdempotencyKey)
            .NotEmpty()
            .WithMessage("The Idempotency-Key header is required.")
            .MaximumLength(MaxIdempotencyKeyLength);

        RuleForEach(command => command.Answers).ChildRules(answer =>
        {
            answer.RuleFor(item => item.QuestionId).NotEmpty();
            answer.RuleFor(item => item.SelectedAnswer).MaximumLength(Attempt.MaxAnswerLength);
        });
    }
}
