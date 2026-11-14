using FluentValidation;

namespace BandUp.Application.Features.Vocabulary.RecordVocabularyReview;

public sealed class RecordVocabularyReviewValidator : AbstractValidator<RecordVocabularyReviewCommand>
{
    public RecordVocabularyReviewValidator()
    {
        RuleFor(command => command.IsCorrect).NotNull().OverridePropertyName("isCorrect");
    }
}
