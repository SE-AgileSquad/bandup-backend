using BandUp.Application.Abstractions;

namespace BandUp.Application.Features.Vocabulary.RecordVocabularyReview;

public sealed record RecordVocabularyReviewCommand(Guid ReviewId, bool? IsCorrect) : ICommand<VocabularyReviewDto>;
