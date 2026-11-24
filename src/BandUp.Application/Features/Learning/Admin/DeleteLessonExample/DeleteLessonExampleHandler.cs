using BandUp.Application.Abstractions;
using BandUp.Application.Common;
using BandUp.Domain.Learning;

namespace BandUp.Application.Features.Learning.Admin.DeleteLessonExample;

public sealed class DeleteLessonExampleHandler(IAdminLessonStore store)
    : ICommandHandler<DeleteLessonExampleCommand, bool>
{
    public async Task<Result<bool>> HandleAsync(DeleteLessonExampleCommand command, CancellationToken cancellationToken)
    {
        Lesson? lesson = await store.LoadForUpdateAsync(command.LessonId, cancellationToken);
        if (lesson is null || !lesson.Examples.Any(example => example.Id == command.ExampleId))
        {
            return Result<bool>.Failure(Error.NotFound("Example not found in this lesson."));
        }

        if (lesson.Status == LessonStatus.Published && lesson.Examples.Count == 1)
        {
            return Result<bool>.Failure(Error.Conflict("A published lesson must retain at least one example."));
        }

        return await store.TryDeleteExampleAsync(lesson.Id, command.ExampleId, cancellationToken)
            ? Result<bool>.Success(true)
            : Result<bool>.Failure(Error.Conflict("The lesson example is referenced by other records."));
    }
}
