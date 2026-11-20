using BandUp.Domain.Content;

namespace BandUp.Application.Features.Learning;

public sealed record LessonSummaryDto(Guid Id, string Title, QuestionType QuestionType, int SortOrder);
