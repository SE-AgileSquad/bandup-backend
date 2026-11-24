using BandUp.Application.Abstractions;
using BandUp.Domain.Content;

namespace BandUp.Application.Features.Learning.Admin.CreateLesson;

public sealed record CreateLessonCommand(LessonInput Input) : ICommand<Guid>;

public sealed record LessonInput(string Title, string Body, QuestionType? QuestionType, int SortOrder);
