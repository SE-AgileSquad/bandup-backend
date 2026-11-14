using BandUp.Application.Abstractions;
using BandUp.Application.Common;

namespace BandUp.Application.Features.Learning.Admin.GetAdminLessonExample;

public sealed class GetAdminLessonExampleHandler(IAdminLessonStore store)
    : IQueryHandler<GetAdminLessonExampleQuery, LessonExampleDetails>
{
    public async Task<Result<LessonExampleDetails>> HandleAsync(
        GetAdminLessonExampleQuery query, CancellationToken cancellationToken) =>
        await store.GetExampleAsync(query.LessonId, query.ExampleId, cancellationToken) is { } example
            ? Result<LessonExampleDetails>.Success(example)
            : Result<LessonExampleDetails>.Failure(Error.NotFound("Example not found in this lesson."));
}
