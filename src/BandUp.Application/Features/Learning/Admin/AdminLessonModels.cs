using BandUp.Domain.Content;
using BandUp.Domain.Learning;
using BandUp.Application.Features.Media.Admin;

namespace BandUp.Application.Features.Learning.Admin;

public sealed record LessonExampleInput(string EvidenceSentence, string Paraphrase, int SortOrder);

public sealed record LessonSummary(
    Guid Id, string Title, QuestionType QuestionType, int SortOrder, LessonStatus Status,
    int ExampleCount, PhotoDetails? Photo)
{
    public bool HasPractice => ExampleCount > 0;
}

public sealed record LessonDetails(
    Guid Id, string Title, string Body, QuestionType QuestionType, int SortOrder, LessonStatus Status,
    IReadOnlyList<LessonExampleDetails> Examples,
    bool CanChangeQuestionType,
    LessonPublicationReadiness PublishReadiness, PhotoDetails? Photo)
{
    public bool HasPractice => Examples.Count > 0;
    public int ExampleCount => Examples.Count;
    public bool IsReadOnly => Status == LessonStatus.Archived;
}

public sealed record LessonExampleDetails(Guid Id, string EvidenceSentence, string Paraphrase, int SortOrder);
