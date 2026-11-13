using BandUp.Application.Abstractions;

namespace BandUp.Application.Features.Learning.Admin.DeleteLesson;

public sealed record DeleteLessonCommand(Guid LessonId) : ICommand<bool>;
