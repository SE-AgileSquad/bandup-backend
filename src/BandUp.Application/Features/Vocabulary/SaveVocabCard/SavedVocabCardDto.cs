namespace BandUp.Application.Features.Vocabulary.SaveVocabCard;

public sealed record SavedVocabCardDto(Guid Id, Guid ReviewId, DateOnly DueDate);
