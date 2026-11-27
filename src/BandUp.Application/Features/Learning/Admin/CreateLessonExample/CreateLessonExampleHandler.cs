using BandUp.Application.Abstractions;
using BandUp.Application.Common;
using BandUp.Domain.Common;
using BandUp.Domain.Learning;

namespace BandUp.Application.Features.Learning.Admin.CreateLessonExample;

public sealed class CreateLessonExampleHandler(IAdminLessonStore store)
    : ICommandHandler<CreateLessonExampleCommand, Guid>
{
    public async Task<Result<Guid>> HandleAsync(CreateLessonExampleCommand command, CancellationToken cancellationToken)
    {
        Lesson? lesson = await store.LoadForUpdateAsync(command.LessonId, cancellationToken);
        if (lesson is null)
        {
            return Result<Guid>.Failure(Error.NotFound("Lesson not found."));
        }

        LessonExampleInput input = command.Input;
        try
        {
            LessonExample example = lesson.AddExample(input.EvidenceSentence, input.Paraphrase, input.SortOrder);
            return Result<Guid>.Success(example.Id);
        }
        catch (DomainException exception)
        {
            return Result<Guid>.Failure(Error.Conflict(exception.Message));
        }
    }
}
