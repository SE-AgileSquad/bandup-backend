using BandUp.Application.Abstractions;

namespace BandUp.Application.Features.Learning.Admin.DeleteLessonExample;

public sealed record DeleteLessonExampleCommand(Guid LessonId, Guid ExampleId) : ICommand<bool>;
