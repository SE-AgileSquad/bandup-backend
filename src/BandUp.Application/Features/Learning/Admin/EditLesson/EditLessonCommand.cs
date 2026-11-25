using BandUp.Application.Abstractions;
using BandUp.Domain.Content;

namespace BandUp.Application.Features.Learning.Admin.EditLesson;

public sealed record EditLessonCommand(Guid LessonId, LessonEditInput Input) : ICommand<bool>;

public sealed record LessonEditInput(string Title, string Body, int SortOrder, QuestionType? QuestionType = null);
