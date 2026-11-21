using BandUp.Application.Abstractions;

namespace BandUp.Application.Features.Vocabulary.SaveVocabCard;

public sealed record SaveVocabCardCommand(string Word, string ContextSentence, Guid? SourceQuestionId)
    : ICommand<SavedVocabCardDto>;
