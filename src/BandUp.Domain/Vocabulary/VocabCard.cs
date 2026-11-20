using BandUp.Domain.Common;

namespace BandUp.Domain.Vocabulary;

public sealed class VocabCard : Entity
{
    private VocabCard()
    {
    }

    public Guid UserId { get; private set; }
    public Guid? SourceQuestionId { get; private set; }
    public string Word { get; private set; } = null!;
    public string ContextSentence { get; private set; } = null!;
    public DateTimeOffset CreatedAt { get; private set; }

    public static VocabCard Create(
        Guid userId,
        string word,
        string contextSentence,
        DateTimeOffset createdAt,
        Guid? sourceQuestionId = null)
    {
        if (userId == Guid.Empty || sourceQuestionId == Guid.Empty)
        {
            throw new DomainException("A vocabulary card requires an owner and a valid question ID when linked.");
        }

        if (string.IsNullOrWhiteSpace(word) || string.IsNullOrWhiteSpace(contextSentence))
        {
            throw new DomainException("A vocabulary card requires a word and a context sentence.");
        }

        return new VocabCard
        {
            UserId = userId,
            SourceQuestionId = sourceQuestionId,
            Word = word.Trim(),
            ContextSentence = contextSentence,
            CreatedAt = createdAt,
        };
    }
}
