using BandUp.Application.Abstractions;
using BandUp.Application.Common;
using BandUp.Domain.Content;
using BandUp.Domain.Learning;
using FluentValidation;

namespace BandUp.Application.Features.Learning.Admin.Mappings;

public sealed record SaveLessonMappingsCommand(SaveLessonMappingsInput Input) : ICommand<bool>;

public sealed class SaveLessonMappingsValidator : AbstractValidator<SaveLessonMappingsCommand>
{
    public SaveLessonMappingsValidator()
    {
        RuleFor(command => command.Input.Mappings).Cascade(CascadeMode.Stop).NotNull()
            .Must(items => items.Length == Enum.GetValues<QuestionType>().Length
                && items.All(item => item is not null && item.QuestionType.HasValue && Enum.IsDefined(item.QuestionType.Value)
                    && item.LessonId != Guid.Empty)
                && items.Select(item => item.QuestionType).Distinct().Count() == items.Length)
            .WithMessage("Supply each supported question type exactly once; use null to clear a default lesson.")
            .OverridePropertyName("mappings");
    }
}

public sealed class SaveLessonMappingsHandler(IAdminLessonMappingStore store)
    : ICommandHandler<SaveLessonMappingsCommand, bool>
{
    public async Task<Result<bool>> HandleAsync(SaveLessonMappingsCommand command, CancellationToken cancellationToken)
    {
        await store.LockChangesAsync(cancellationToken);
        LessonMappingSelection[] selections = command.Input.Mappings;
        Guid[] ids = selections.Where(item => item.LessonId.HasValue)
            .Select(item => item.LessonId!.Value).Distinct().Order().ToArray();
        IReadOnlyList<Lesson> lessons = await store.LoadLessonsForUpdateAsync(ids, cancellationToken);
        Dictionary<string, string[]> errors = [];
        for (int index = 0; index < selections.Length; index++)
        {
            LessonMappingSelection selection = selections[index];
            if (selection.LessonId is not { } id)
            {
                continue;
            }

            Lesson? lesson = lessons.SingleOrDefault(item => item.Id == id);
            if (lesson is null || lesson.Status != LessonStatus.Published || lesson.QuestionType != selection.QuestionType)
            {
                errors[$"mappings[{index}].lessonId"] = ["Select an existing published lesson with the same question type."];
            }
        }

        if (errors.Count != 0)
        {
            return Result<bool>.Failure(Error.Validation(errors));
        }

        await store.SetDefaultsAsync(selections, lessons, cancellationToken);
        return Result<bool>.Success(true);
    }
}
