using BandUp.Application.Abstractions;
using BandUp.Application.Common;

namespace BandUp.Application.Features.Learning.Admin.ListAdminLessons;

public sealed class ListAdminLessonsHandler(IAdminLessonStore store)
    : IQueryHandler<ListAdminLessonsQuery, PagedResult<LessonSummary>>
{
    public async Task<Result<PagedResult<LessonSummary>>> HandleAsync(
        ListAdminLessonsQuery query, CancellationToken cancellationToken) =>
        Result<PagedResult<LessonSummary>>.Success(await store.ListAsync(query.Page, cancellationToken));
}
