namespace BandUp.Application.Features.Vocabulary.RecordVocabularyReview;

public sealed record VocabularyReviewDto(
    Guid Id, int LeitnerBox, DateOnly DueDate, int StreakCorrect, DateTimeOffset? LastReviewedAt);
