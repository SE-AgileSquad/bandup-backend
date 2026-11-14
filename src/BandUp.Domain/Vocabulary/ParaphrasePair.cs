using BandUp.Domain.Common;

namespace BandUp.Domain.Vocabulary;

public sealed class ParaphrasePair : Entity
{
    private ParaphrasePair()
    {
    }

    public Guid? QuestionId { get; private set; }
    public string SourceSentence { get; private set; } = null!;
    public string EquivalentSentence { get; private set; } = null!;
    public ParaphrasePairStatus Status { get; private set; } = ParaphrasePairStatus.Draft;
    public Guid CreatedBy { get; private set; }

    public static ParaphrasePair CreateDraft(
        string sourceSentence,
        string equivalentSentence,
        Guid createdBy,
        Guid? questionId = null)
    {
        if (createdBy == Guid.Empty || questionId == Guid.Empty)
        {
            throw new DomainException("A paraphrase pair requires a creator and a valid question ID when linked.");
        }

        ParaphrasePair pair = new() { CreatedBy = createdBy, QuestionId = questionId };
        pair.Edit(sourceSentence, equivalentSentence);
        return pair;
    }

    public void Edit(string sourceSentence, string equivalentSentence)
    {
        if (string.IsNullOrWhiteSpace(sourceSentence) || string.IsNullOrWhiteSpace(equivalentSentence))
        {
            throw new DomainException("A paraphrase pair requires a source sentence and an equivalent sentence.");
        }

        if (SourceSentence == sourceSentence && EquivalentSentence == equivalentSentence)
        {
            return;
        }

        SourceSentence = sourceSentence;
        EquivalentSentence = equivalentSentence;
        Status = ParaphrasePairStatus.Draft;
    }

    public void Approve() => Status = ParaphrasePairStatus.Approved;
}
