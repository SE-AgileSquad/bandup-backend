using BandUp.Application.Abstractions;

namespace BandUp.Application.Features.Learning.Admin.ArchiveLesson;

public sealed record ArchiveLessonCommand(Guid LessonId) : ICommand<bool>;
