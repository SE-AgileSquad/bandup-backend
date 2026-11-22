using BandUp.Domain.Common;

namespace BandUp.Domain.Content;

public sealed class Passage : Entity
{
    private readonly List<Question> _questions = [];

    private Passage()
    {
    }

    public string Title { get; private set; } = null!;
    public string Body { get; private set; } = null!;
    public string SourceNote { get; private set; } = null!;
    public string UsageBasis { get; private set; } = null!;
    public PassageStatus Status { get; private set; } = PassageStatus.Draft;
    public int Version { get; private set; } = 1;
    public Guid? PreviousVersionId { get; private set; }
    public Guid CreatedBy { get; private set; }
    public DateTimeOffset? PublishedAt { get; private set; }
    public DateTimeOffset? UpdatedAt { get; private set; }
    public IReadOnlyList<Question> Questions => _questions.AsReadOnly();

    public static Passage CreateDraft(
        string title,
        string body,
        string sourceNote,
        string usageBasis,
        Guid createdBy)
    {
        if (createdBy == Guid.Empty)
        {
            throw new DomainException("A passage must have a creator.");
        }

        Passage passage = new() { CreatedBy = createdBy };
        passage.EditDraft(title, body, sourceNote, usageBasis);
        return passage;
    }

    public void EditDraft(string title, string body, string sourceNote, string usageBasis)
    {
        EnsureDraft();

        if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(body)
            || string.IsNullOrWhiteSpace(sourceNote) || string.IsNullOrWhiteSpace(usageBasis))
        {
            throw new DomainException("A passage requires a title, body, source note and usage basis.");
        }

        if (!string.Equals(Body, body, StringComparison.Ordinal))
        {
            foreach (Question question in _questions)
            {
                question.InvalidateAnswerKeyVerification();
            }
        }

        Title = title;
        Body = body;
        SourceNote = sourceNote;
        UsageBasis = usageBasis;
    }

    public Question AddQuestion(QuestionType type, string prompt, int sortOrder)
    {
        EnsureDraft();

        Question question = new(Id, type, prompt, sortOrder);
        _questions.Add(question);
        return question;
    }

    public void Publish(DateTimeOffset publishedAt)
    {
        EnsureDraft();

        PublicationIssue? issue = GetPublicationIssues().FirstOrDefault();
        if (issue is not null)
        {
            throw new DomainException(issue.Message);
        }

        foreach (Question question in _questions)
        {
            question.MarkPublished();
        }

        Status = PassageStatus.Published;
        PublishedAt = publishedAt;
    }

    public IReadOnlyList<PublicationIssue> GetPublicationIssues()
    {
        List<PublicationIssue> issues = [];
        if (Status != PassageStatus.Draft)
        {
            issues.Add(new("status", "Only a draft passage can be published."));
        }

        if (_questions.Count == 0)
        {
            issues.Add(new("questions", "A passage must have at least one question to be published."));
        }

        foreach (Question question in _questions)
        {
            issues.AddRange(question.GetPublicationIssues());
        }

        return issues;
    }

    public void Archive()
    {
        if (Status != PassageStatus.Published)
        {
            throw new DomainException("Only a published passage can be archived.");
        }

        Status = PassageStatus.Archived;
    }

    public Passage CreateRevision(Guid createdBy)
    {
        if (Status == PassageStatus.Draft)
        {
            throw new DomainException("Only a previously published passage can be revised.");
        }

        Passage revision = CreateDraft(Title, Body, SourceNote, UsageBasis, createdBy);
        revision.Version = checked(Version + 1);
        revision.PreviousVersionId = Id;

        foreach (Question question in _questions)
        {
            revision._questions.Add(question.CopyTo(revision.Id));
        }

        return revision;
    }

    private void EnsureDraft()
    {
        if (Status != PassageStatus.Draft)
        {
            throw new DomainException("Only a draft passage can be changed.");
        }
    }
}
