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
    public IReadOnlyList<LessonExample> Examples => _examples.AsReadOnly();

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

    public void Edit(string title, string body, int sortOrder)
    {
        if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(body))
        {
            throw new DomainException("A lesson requires a title and body.");
        }

        Title = title;
        Body = body;
        SortOrder = sortOrder;
    }

    public LessonExample AddExample(string evidenceSentence, string paraphrase, int sortOrder)
    {
        LessonExample example = new(Id, evidenceSentence, paraphrase, sortOrder);
        _examples.Add(example);
        return example;
    }

    public void Publish()
    {
        if (Status != LessonStatus.Draft)
        {
            throw new DomainException("Only a draft lesson can be published.");
        }

        if (_examples.Count == 0)
        {
            throw new DomainException("A lesson requires at least one example before publication.");
        }

        Status = LessonStatus.Published;
    }
}
