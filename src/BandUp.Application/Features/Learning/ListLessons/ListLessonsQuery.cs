using BandUp.Application.Abstractions;
using BandUp.Application.Common;
using BandUp.Domain.Content;

namespace BandUp.Application.Features.Learning.ListLessons;

public sealed record ListLessonsQuery(
    QuestionType? QuestionType = null,
    int Page = 1,
    int PageSize = PageRequest.DefaultPageSize)
    : IQuery<PagedResult<LessonSummaryDto>>;
