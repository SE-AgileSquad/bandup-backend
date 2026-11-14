using BandUp.Application.Abstractions;
using BandUp.Application.Common;
using BandUp.Domain.Learning;

namespace BandUp.Application.Features.Learning.Admin.EditLesson;

public sealed class EditLessonHandler(IAdminLessonStore store)
    : ICommandHandler<EditLessonCommand, bool>
{
    public async Task<Result<bool>> HandleAsync(EditLessonCommand command, CancellationToken cancellationToken)
    {
        Lesson? lesson = await store.LoadForUpdateAsync(command.LessonId, cancellationToken);
        if (lesson is null)
        {
            return Result<bool>.Failure(Error.NotFound("Lesson not found."));
        }

        LessonEditInput input = command.Input;
        lesson.Edit(input.Title, input.Body, input.SortOrder);
        return Result<bool>.Success(true);
    }
}
