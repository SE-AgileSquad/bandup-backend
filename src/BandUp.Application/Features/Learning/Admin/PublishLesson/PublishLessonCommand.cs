using BandUp.Application.Abstractions;

namespace BandUp.Application.Features.Learning.Admin.PublishLesson;

public sealed record PublishLessonCommand(Guid LessonId) : ICommand<bool>;
