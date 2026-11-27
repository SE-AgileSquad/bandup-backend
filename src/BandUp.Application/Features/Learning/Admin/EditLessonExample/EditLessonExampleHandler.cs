using BandUp.Application.Abstractions;
using BandUp.Application.Common;
using BandUp.Domain.Common;
using BandUp.Domain.Learning;

namespace BandUp.Application.Features.Learning.Admin.EditLessonExample;

public sealed class EditLessonExampleHandler(IAdminLessonStore store)
    : ICommandHandler<EditLessonExampleCommand, bool>
{
    public async Task<Result<bool>> HandleAsync(EditLessonExampleCommand command, CancellationToken cancellationToken)
    {
        Lesson? lesson = await store.LoadForUpdateAsync(command.LessonId, cancellationToken);
        LessonExample? example = lesson?.Examples.SingleOrDefault(item => item.Id == command.ExampleId);
        if (example is null)
        {
            return Result<bool>.Failure(Error.NotFound("Example not found in this lesson."));
        }

        LessonExampleInput input = command.Input;
        try
        {
            lesson!.EditExample(example.Id, input.EvidenceSentence, input.Paraphrase, input.SortOrder);
            return Result<bool>.Success(true);
        }
        catch (DomainException exception)
        {
            return Result<bool>.Failure(Error.Conflict(exception.Message));
        }
    }
}
