using BandUp.Domain.Content;
using BandUp.Domain.Learning;

namespace BandUp.Application.Features.Learning.Admin;

public sealed record LessonExampleInput(string EvidenceSentence, string Paraphrase, int SortOrder);

public sealed record LessonSummary(
    Guid Id, string Title, QuestionType QuestionType, int SortOrder, LessonStatus Status,
    int ExampleCount)
{
    public bool HasPractice => ExampleCount > 0;
}

public sealed record LessonDetails(
    Guid Id, string Title, string Body, QuestionType QuestionType, int SortOrder, LessonStatus Status,
    IReadOnlyList<LessonExampleDetails> Examples)
{
    public bool HasPractice => Examples.Count > 0;
}

public sealed record LessonExampleDetails(Guid Id, string EvidenceSentence, string Paraphrase, int SortOrder);
