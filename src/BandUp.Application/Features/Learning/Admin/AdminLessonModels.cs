using BandUp.Domain.Content;
using BandUp.Domain.Learning;

namespace BandUp.Application.Features.Learning.Admin;

public sealed record LessonExampleInput(string EvidenceSentence, string Paraphrase, int SortOrder);

public sealed record LessonSummary(Guid Id, string Title, QuestionType QuestionType, int SortOrder, LessonStatus Status);

public sealed record LessonDetails(
    Guid Id, string Title, string Body, QuestionType QuestionType, int SortOrder, LessonStatus Status,
    IReadOnlyList<LessonExampleDetails> Examples);

public sealed record LessonExampleDetails(Guid Id, string EvidenceSentence, string Paraphrase, int SortOrder);
