using BandUp.Application.Abstractions;
using BandUp.Application.Common;

namespace BandUp.Application.Features.Learning.GetLesson;

public sealed class GetLessonHandler(ILessonQueries lessonQueries) : IQueryHandler<GetLessonQuery, LessonDto>
{
    public async Task<Result<LessonDto>> HandleAsync(GetLessonQuery query, CancellationToken cancellationToken)
    {
        LessonDto? lesson = await lessonQueries.GetPublishedLessonAsync(query.LessonId, cancellationToken);

        return lesson is null
            ? Result<LessonDto>.Failure(Error.NotFound("The lesson does not exist or is not available."))
            : Result<LessonDto>.Success(lesson);
    }
}
