using BandUp.Application.Abstractions;
using BandUp.Application.Common;
using BandUp.Domain.Learning;

namespace BandUp.Application.Features.Learning.Admin.ReorderLessonExamples;

public sealed class ReorderLessonExamplesHandler(IAdminLessonStore store) : ICommandHandler<ReorderLessonExamplesCommand, bool>
{
    public async Task<Result<bool>> HandleAsync(ReorderLessonExamplesCommand command, CancellationToken cancellationToken)
    {
        Lesson? lesson = await store.LoadForUpdateAsync(command.LessonId, cancellationToken);
        if (lesson is null)
        {
            return Result<bool>.Failure(Error.NotFound("Lesson not found."));
        }

        if (lesson.Status == LessonStatus.Archived)
        {
            return Result<bool>.Failure(Error.Conflict("An archived lesson is read-only."));
        }

        Guid[] ids = command.Input.ExampleIds;
        if (ids.Length != lesson.Examples.Count || !ids.ToHashSet().SetEquals(lesson.Examples.Select(example => example.Id)))
        {
            return Result<bool>.Failure(Error.Validation(new Dictionary<string, string[]>
            {
                ["exampleIds"] = ["Supply every example in this lesson exactly once."],
            }));
        }

        lesson.ReorderExamples(ids);
        return Result<bool>.Success(true);
    }
}
