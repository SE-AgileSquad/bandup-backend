using BandUp.Domain.Common;
using BandUp.Domain.Content;

namespace BandUp.Domain.Learning;

public sealed class Lesson : Entity
{
    private readonly List<LessonExample> _examples = [];

    private Lesson()
    {
    }

    public string Title { get; private set; } = null!;
    public string Body { get; private set; } = null!;
    public QuestionType QuestionType { get; private set; }
    public int SortOrder { get; private set; }
    public LessonStatus Status { get; private set; } = LessonStatus.Draft;
    public Guid? PhotoId { get; private set; }
    public IReadOnlyList<LessonExample> Examples => _examples.AsReadOnly();

    public void SetPhoto(Guid? photoId)
    {
        EnsureEditable();
        if (photoId == Guid.Empty)
        {
            throw new DomainException("A photo ID cannot be empty.");
        }
        PhotoId = photoId;
    }

    public static Lesson CreateDraft(string title, string body, QuestionType questionType, int sortOrder)
    {
        if (!Enum.IsDefined(questionType))
        {
            throw new DomainException("The lesson question type is invalid.");
        }

        Lesson lesson = new() { QuestionType = questionType };
        lesson.Edit(title, body, sortOrder);
        return lesson;
    }

    public void Edit(string title, string body, int sortOrder, QuestionType? questionType = null)
    {
        EnsureEditable();

        if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(body))
        {
            throw new DomainException("A lesson requires a title and body.");
        }

        if (questionType.HasValue && !Enum.IsDefined(questionType.Value))
        {
            throw new DomainException("The lesson question type is invalid.");
        }

        if (questionType.HasValue && questionType.Value != QuestionType && Status != LessonStatus.Draft)
        {
            throw new DomainException("The question type can only be changed in a draft lesson.");
        }

        Title = title;
        Body = body;
        SortOrder = sortOrder;
        QuestionType = questionType ?? QuestionType;
    }

    public LessonExample AddExample(string evidenceSentence, string paraphrase, int sortOrder)
    {
        EnsureEditable();

        LessonExample example = new(Id, evidenceSentence, paraphrase, sortOrder);
        _examples.Add(example);
        return example;
    }

    public void EditExample(Guid exampleId, string evidenceSentence, string paraphrase, int sortOrder)
    {
        EnsureEditable();
        LessonExample example = _examples.SingleOrDefault(item => item.Id == exampleId)
            ?? throw new DomainException("Example not found in this lesson.");
        example.Edit(evidenceSentence, paraphrase, sortOrder);
    }

    public void ReorderExamples(IReadOnlyList<Guid> exampleIds)
    {
        EnsureEditable();
        if (exampleIds.Count != _examples.Count || exampleIds.Distinct().Count() != exampleIds.Count
            || !exampleIds.ToHashSet().SetEquals(_examples.Select(example => example.Id)))
        {
            throw new DomainException("Supply every example in this lesson exactly once.");
        }

        Dictionary<Guid, LessonExample> byId = _examples.ToDictionary(example => example.Id);
        for (int index = 0; index < exampleIds.Count; index++)
        {
            LessonExample example = byId[exampleIds[index]];
            example.Edit(example.EvidenceSentence, example.Paraphrase, index + 1);
        }
    }

    public void Publish()
    {
        LessonPublicationIssue? issue = GetPublicationIssues().FirstOrDefault();
        if (issue is not null)
        {
            throw new DomainException(issue.Message);
        }

        Status = LessonStatus.Published;
    }

    public IReadOnlyList<LessonPublicationIssue> GetPublicationIssues()
    {
        List<LessonPublicationIssue> issues = [];
        if (Status != LessonStatus.Draft)
        {
            issues.Add(new("status", "Only a draft lesson can be published."));
        }

        if (string.IsNullOrWhiteSpace(Title))
        {
            issues.Add(new("title", "A lesson title is required."));
        }

        if (string.IsNullOrWhiteSpace(Body))
        {
            issues.Add(new("body", "The lesson text is required."));
        }

        if (_examples.Count == 0)
        {
            issues.Add(new("examples", "Add at least one example before publishing the lesson."));
        }

        foreach (LessonExample example in _examples)
        {
            if (string.IsNullOrWhiteSpace(example.EvidenceSentence))
            {
                issues.Add(new($"examples.{example.Id}.evidenceSentence", "An evidence sentence is required."));
            }

            if (string.IsNullOrWhiteSpace(example.Paraphrase))
            {
                issues.Add(new($"examples.{example.Id}.paraphrase", "A paraphrase is required."));
            }
        }

        return issues;
    }

    public void Archive()
    {
        if (Status != LessonStatus.Published)
        {
            throw new DomainException("Only a published lesson can be archived.");
        }

        Status = LessonStatus.Archived;
    }

    private void EnsureEditable()
    {
        if (Status == LessonStatus.Archived)
        {
            throw new DomainException("An archived lesson is read-only.");
        }
    }
}
