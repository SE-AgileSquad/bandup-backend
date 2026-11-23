using BandUp.Application.Abstractions;
using BandUp.Application.Common;
using BandUp.Domain.Common;
using BandUp.Domain.Learning;

namespace BandUp.Application.Features.Learning.Admin.PublishLesson;

public sealed class PublishLessonHandler(IAdminLessonStore store)
    : ICommandHandler<PublishLessonCommand, bool>
{
    public async Task<Result<bool>> HandleAsync(PublishLessonCommand command, CancellationToken cancellationToken)
    {
        Lesson? lesson = await store.LoadForUpdateAsync(command.LessonId, cancellationToken);
        if (lesson is null)
        {
            return Result<bool>.Failure(Error.NotFound("Lesson not found."));
        }

        if (lesson.Status == LessonStatus.Published)
        {
            return Result<bool>.Success(true);
        }

        try
        {
            lesson.Publish();
            return Result<bool>.Success(true);
        }
        catch (DomainException exception)
        {
            return Result<bool>.Failure(Error.Conflict(exception.Message));
        }
    }
}
