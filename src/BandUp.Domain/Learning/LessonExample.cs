using BandUp.Domain.Common;

namespace BandUp.Domain.Learning;

public sealed class LessonExample : Entity
{
    private LessonExample()
    {
    }

    internal LessonExample(Guid lessonId, string evidenceSentence, string paraphrase, int sortOrder)
    {
        LessonId = lessonId;
        Edit(evidenceSentence, paraphrase, sortOrder);
    }

    public Guid LessonId { get; private set; }
    public string EvidenceSentence { get; private set; } = null!;
    public string Paraphrase { get; private set; } = null!;
    public int SortOrder { get; private set; }

    public void Edit(string evidenceSentence, string paraphrase, int sortOrder)
    {
        if (string.IsNullOrWhiteSpace(evidenceSentence) || string.IsNullOrWhiteSpace(paraphrase))
        {
            throw new DomainException("A lesson example requires an evidence sentence and a paraphrase.");
        }

        EvidenceSentence = evidenceSentence;
        Paraphrase = paraphrase;
        SortOrder = sortOrder;
    }
}
