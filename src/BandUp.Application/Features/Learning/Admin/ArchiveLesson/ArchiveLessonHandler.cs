using BandUp.Application.Abstractions;
using BandUp.Application.Common;
using BandUp.Domain.Learning;

namespace BandUp.Application.Features.Learning.Admin.ArchiveLesson;

public sealed class ArchiveLessonHandler(IAdminLessonStore store) : ICommandHandler<ArchiveLessonCommand, bool>
{
    public async Task<Result<bool>> HandleAsync(ArchiveLessonCommand command, CancellationToken cancellationToken)
    {
        Lesson? lesson = await store.LoadForUpdateAsync(command.LessonId, cancellationToken);
        if (lesson is null)
        {
            return Result<bool>.Failure(Error.NotFound("Lesson not found."));
        }

        if (lesson.Status == LessonStatus.Archived)
        {
            return Result<bool>.Success(true);
        }

        if (lesson.Status != LessonStatus.Published)
        {
            return Result<bool>.Failure(Error.Conflict("Only a published lesson can be archived."));
        }

        lesson.Archive();
        return Result<bool>.Success(true);
    }
}
