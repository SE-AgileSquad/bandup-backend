namespace BandUp.Application.Features.Vocabulary.ListDueReviews;

public sealed record DueVocabularyReviewDto(
    Guid Id,
    Guid VocabCardId,
    string Word,
    string ContextSentence,
    DateOnly DueDate,
    int LeitnerBox);
