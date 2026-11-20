using FluentValidation;

namespace BandUp.Application.Features.Exams.StartAttempt;

public sealed class StartAttemptValidator : AbstractValidator<StartAttemptCommand>
{
    public StartAttemptValidator() =>
        RuleFor(command => command.PassageId).NotEmpty();
}
