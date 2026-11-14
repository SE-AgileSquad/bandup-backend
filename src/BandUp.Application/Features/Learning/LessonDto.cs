using BandUp.Domain.Content;

namespace BandUp.Application.Features.Learning;

public sealed record LessonDto(
    Guid Id,
    string Title,
    string Body,
    QuestionType QuestionType,
    int SortOrder,
    IReadOnlyList<LessonExampleDto> Examples);
