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
        string? explanation,
        string? evidenceSentence = null,
        string? misleadingSentence = null,
        string? unspecifiedPart = null)
    {
        EnsureDraft();

        explanation = NormalizeOptional(explanation);
        evidenceSentence = NormalizeOptional(evidenceSentence);
        misleadingSentence = NormalizeOptional(misleadingSentence);
        unspecifiedPart = NormalizeOptional(unspecifiedPart);

        if (Explanation == explanation && EvidenceSentence == evidenceSentence
            && MisleadingSentence == misleadingSentence && UnspecifiedPart == unspecifiedPart)
        {
            return;
        }

        Explanation = explanation;
        EvidenceSentence = evidenceSentence;
        MisleadingSentence = misleadingSentence;
        UnspecifiedPart = unspecifiedPart;
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

        PublicationIssue? issue = GetPublicationIssues(requireVerification: false).FirstOrDefault();
        if (issue is not null)
        {
            throw new DomainException(issue.Message);
        }

        AnswerKey!.Verify(verifiedBy, verifiedAt);
    }

    public void ClearAnswerKey()
    {
        EnsureDraft();
        AnswerKey = null;
    }

    public void ClearAnswerKeyVerification()
    {
        EnsureDraft();
        InvalidateAnswerKeyVerification();
    }

    public IReadOnlyList<PublicationIssue> GetPublicationIssues(bool requireVerification = true)
    {
        List<PublicationIssue> issues = [];
        if (Status != QuestionStatus.Draft)
        {
            issues.Add(new("status", "Only a draft question can be published or verified.", Id));
        }

        if (AnswerKey is null)
        {
            issues.Add(new("answerKey.correctAnswer", "An answer key is required.", Id));
        }

        if (string.IsNullOrWhiteSpace(Explanation))
        {
            issues.Add(new("explanation", "An explanation is required.", Id));
        }

        if (AnswerKey is not null)
        {
            if (AnswerKey.IsNotGiven(Type))
            {
                if (UnspecifiedPart is null)
                {
                    issues.Add(new("unspecifiedPart", "Identify the information not stated in the passage for Not Given.", Id));
                }

                if (EvidenceSentence is not null)
                {
                    issues.Add(new("evidenceSentence", "Use the unspecified part instead of evidence for Not Given.", Id));
                }
            }
            else
            {
                if (EvidenceSentence is null)
                {
                    issues.Add(new("evidenceSentence", "An evidence sentence from the passage is required.", Id));
                }

                if (UnspecifiedPart is not null)
                {
                    issues.Add(new("unspecifiedPart", "The unspecified part is only allowed for Not Given.", Id));
                }
            }
        }

        if (requireVerification && AnswerKey is not { IsVerified: true })
        {
            issues.Add(new("answerKey.isVerified", "The answer, explanation and evidence must be checked before publication.", Id));
        }

        return issues;
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

    private static string? NormalizeOptional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private void EnsureDraft()
    {
        if (Status != QuestionStatus.Draft)
        {
            throw new DomainException("Only a draft question can be changed.");
        }
    }
}
