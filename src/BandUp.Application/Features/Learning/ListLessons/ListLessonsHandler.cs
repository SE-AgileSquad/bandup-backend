using BandUp.Application.Abstractions;
using BandUp.Application.Common;

namespace BandUp.Application.Features.Learning.ListLessons;

public sealed class ListLessonsHandler(ILessonQueries lessonQueries)
    : IQueryHandler<ListLessonsQuery, PagedResult<LessonSummaryDto>>
{
    public async Task<Result<PagedResult<LessonSummaryDto>>> HandleAsync(
        ListLessonsQuery query,
        CancellationToken cancellationToken)
    {
        PagedResult<LessonSummaryDto> page = await lessonQueries.ListPublishedLessonsAsync(
            query.QuestionType,
            new PageRequest(query.Page, query.PageSize),
            cancellationToken);

        return Result<PagedResult<LessonSummaryDto>>.Success(page);
    }
}
