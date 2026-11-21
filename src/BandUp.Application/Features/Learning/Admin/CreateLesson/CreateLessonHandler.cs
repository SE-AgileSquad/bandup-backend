using BandUp.Application.Abstractions;
using BandUp.Application.Common;
using BandUp.Domain.Learning;

namespace BandUp.Application.Features.Learning.Admin.CreateLesson;

public sealed class CreateLessonHandler(IAdminLessonStore store)
    : ICommandHandler<CreateLessonCommand, Guid>
{
    public async Task<Result<Guid>> HandleAsync(CreateLessonCommand command, CancellationToken cancellationToken)
    {
        LessonInput input = command.Input;
        Lesson draft = Lesson.CreateDraft(input.Title, input.Body, input.QuestionType!.Value, input.SortOrder);
        await store.AddAsync(draft, cancellationToken);
        return Result<Guid>.Success(draft.Id);
    }
}
