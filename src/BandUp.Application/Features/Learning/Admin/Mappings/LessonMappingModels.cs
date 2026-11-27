using BandUp.Domain.Content;
using BandUp.Domain.Learning;

namespace BandUp.Application.Features.Learning.Admin.Mappings;

public sealed record LessonMappingSelection(QuestionType? QuestionType, Guid? LessonId);
public sealed record SaveLessonMappingsInput(LessonMappingSelection[] Mappings);
public sealed record LessonMappingOption(Guid Id, string Title);
public sealed record MappedLesson(Guid Id, string Title, LessonStatus Status);
public sealed record LessonMappingRow(
    QuestionType QuestionType, MappedLesson? SelectedLesson, IReadOnlyList<LessonMappingOption> Options)
{
    public bool IsEnabled => SelectedLesson?.Status == LessonStatus.Published;
}
