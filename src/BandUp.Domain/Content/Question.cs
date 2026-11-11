using BandUp.Domain.Common;

namespace BandUp.Domain.Content;

public sealed class Question : Entity
{
    private Question()
    {
    }

    internal Question(Guid passageId, QuestionType type, string prompt, int sortOrder)
    {
        if (!Enum.IsDefined(type))
        {
            throw new DomainException("The question type is invalid.");
        }

        PassageId = passageId;
        Type = type;
        EditDraft(prompt, sortOrder);
    }

    public Guid PassageId { get; private set; }
    public QuestionType Type { get; private set; }
    public string Prompt { get; private set; } = null!;
    public int SortOrder { get; private set; }
    public string? Explanation { get; private set; }
    public string? EvidenceSentence { get; private set; }
    public string? MisleadingSentence { get; private set; }
    public string? UnspecifiedPart { get; private set; }
    public QuestionStatus Status { get; private set; } = QuestionStatus.Draft;
    public AnswerKey? AnswerKey { get; private set; }

    public void EditDraft(string prompt, int sortOrder)
    {
        EnsureDraft();

        if (string.IsNullOrWhiteSpace(prompt))
        {
            throw new DomainException("A question requires a prompt.");
        }

        if (!string.Equals(Prompt, prompt, StringComparison.Ordinal))
        {
            InvalidateAnswerKeyVerification();
        }

        Prompt = prompt;
        SortOrder = sortOrder;
    }

    public void SetExplanation(
        string explanation,
        string? evidenceSentence = null,
        string? misleadingSentence = null,
        string? unspecifiedPart = null)
    {
        EnsureDraft();

        if (string.IsNullOrWhiteSpace(explanation))
        {
            throw new DomainException("A question requires an explanation before publication.");
        }

        Explanation = explanation;
        EvidenceSentence = string.IsNullOrWhiteSpace(evidenceSentence) ? null : evidenceSentence;
        MisleadingSentence = string.IsNullOrWhiteSpace(misleadingSentence) ? null : misleadingSentence;
        UnspecifiedPart = string.IsNullOrWhiteSpace(unspecifiedPart) ? null : unspecifiedPart;
        InvalidateAnswerKeyVerification();
    }

    public void SetAnswerKey(string correctAnswer, IEnumerable<string>? acceptedVariants = null)
    {
        EnsureDraft();

        if (AnswerKey is null)
        {
            AnswerKey = new AnswerKey(Id, Type, correctAnswer, acceptedVariants);
            return;
        }

        AnswerKey.SetAnswer(Type, correctAnswer, acceptedVariants);
    }

    public void VerifyAnswerKey(Guid verifiedBy, DateTimeOffset verifiedAt)
    {
        EnsureDraft();

        if (AnswerKey is null)
        {
            throw new DomainException("A question must have an answer key before it can be verified.");
        }

        EnsureExplanationIsComplete(AnswerKey);
        AnswerKey.Verify(verifiedBy, verifiedAt);
    }

    internal void EnsurePublishable()
    {
        EnsureDraft();

        if (AnswerKey is not { IsVerified: true })
        {
            throw new DomainException("A question requires a verified answer key and an explanation to be published.");
        }
    }

    private void EnsureExplanationIsComplete(AnswerKey answerKey)
    {
        if (string.IsNullOrWhiteSpace(Explanation))
        {
            throw new DomainException("A question requires an explanation before verification.");
        }

        if (answerKey.IsNotGiven(Type))
        {
            if (UnspecifiedPart is null || EvidenceSentence is not null)
            {
                throw new DomainException("A Not Given question requires the unspecified part in place of evidence.");
            }

            return;
        }

        if (EvidenceSentence is null || UnspecifiedPart is not null)
        {
            throw new DomainException("A question other than Not Given requires evidence and no unspecified part.");
        }
    }

    internal void MarkPublished() => Status = QuestionStatus.Published;

    internal void InvalidateAnswerKeyVerification() => AnswerKey?.InvalidateVerification();

    internal Question CopyTo(Guid passageId)
    {
        Question copy = new(passageId, Type, Prompt, SortOrder)
        {
            Explanation = Explanation,
            EvidenceSentence = EvidenceSentence,
            MisleadingSentence = MisleadingSentence,
            UnspecifiedPart = UnspecifiedPart,
        };

        if (AnswerKey is { } answerKey)
        {
            copy.SetAnswerKey(answerKey.CorrectAnswer, answerKey.AcceptedVariants);
        }

        return copy;
    }

    private void EnsureDraft()
    {
        if (Status != QuestionStatus.Draft)
        {
            throw new DomainException("Only a draft question can be changed.");
        }
    }
}
