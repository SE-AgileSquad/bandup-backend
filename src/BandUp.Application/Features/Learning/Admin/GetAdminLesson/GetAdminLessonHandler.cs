using BandUp.Application.Abstractions;
using BandUp.Application.Common;

namespace BandUp.Application.Features.Learning.Admin.GetAdminLesson;

public sealed class GetAdminLessonHandler(IAdminLessonStore store)
    : IQueryHandler<GetAdminLessonQuery, LessonDetails>
{
    public async Task<Result<LessonDetails>> HandleAsync(GetAdminLessonQuery query, CancellationToken cancellationToken) =>
        await store.GetAsync(query.LessonId, cancellationToken) is { } lesson
            ? Result<LessonDetails>.Success(lesson)
            : Result<LessonDetails>.Failure(Error.NotFound("Lesson not found."));
}
