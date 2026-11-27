using BandUp.Application.Abstractions;
using BandUp.Application.Common;

namespace BandUp.Application.Features.Learning.Admin.ListAdminLessonExamples;

public sealed class ListAdminLessonExamplesHandler(IAdminLessonStore store)
    : IQueryHandler<ListAdminLessonExamplesQuery, IReadOnlyList<LessonExampleDetails>>
{
    public async Task<Result<IReadOnlyList<LessonExampleDetails>>> HandleAsync(
        ListAdminLessonExamplesQuery query, CancellationToken cancellationToken) =>
        await store.GetAsync(query.LessonId, cancellationToken) is { } lesson
            ? Result<IReadOnlyList<LessonExampleDetails>>.Success(lesson.Examples)
            : Result<IReadOnlyList<LessonExampleDetails>>.Failure(Error.NotFound("Lesson not found."));
}
