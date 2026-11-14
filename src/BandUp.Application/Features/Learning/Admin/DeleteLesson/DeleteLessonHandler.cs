using BandUp.Application.Abstractions;
using BandUp.Application.Common;
using BandUp.Domain.Learning;

namespace BandUp.Application.Features.Learning.Admin.DeleteLesson;

public sealed class DeleteLessonHandler(IAdminLessonStore store)
    : ICommandHandler<DeleteLessonCommand, bool>
{
    public async Task<Result<bool>> HandleAsync(DeleteLessonCommand command, CancellationToken cancellationToken)
    {
        Lesson? lesson = await store.LoadForUpdateAsync(command.LessonId, cancellationToken);
        if (lesson is null)
        {
            return Result<bool>.Failure(Error.NotFound("Lesson not found."));
        }

        return await store.TryDeleteLessonAsync(lesson.Id, cancellationToken)
            ? Result<bool>.Success(true)
            : Result<bool>.Failure(Error.Conflict("The lesson is referenced by a lesson mapping or learner progress."));
    }
}
