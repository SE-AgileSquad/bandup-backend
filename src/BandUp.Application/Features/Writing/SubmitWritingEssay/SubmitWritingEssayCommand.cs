using BandUp.Application.Abstractions;
using BandUp.Domain.Writing;
using FluentValidation;

namespace BandUp.Application.Features.Writing.SubmitWritingEssay;

public sealed record SubmitWritingEssayCommand(Guid IdempotencyKey, Guid PromptId, string EssayText)
    : ICommand<WritingSubmissionAcceptedDto>;

public sealed class SubmitWritingEssayValidator : AbstractValidator<SubmitWritingEssayCommand>
{
    public SubmitWritingEssayValidator()
    {
        RuleFor(c => c.IdempotencyKey).NotEmpty();
        RuleFor(c => c.PromptId).NotEmpty();
        RuleFor(c => c.EssayText).Must(EssayPolicy.IsStructurallySafe)
            .WithMessage("Essay text must contain valid Unicode and fit the character limit.");
    }
}

public sealed class WritingAcceptanceSettings
{
    public TimeSpan QueueTimeout { get; set; } = TimeSpan.FromMinutes(5);
}
