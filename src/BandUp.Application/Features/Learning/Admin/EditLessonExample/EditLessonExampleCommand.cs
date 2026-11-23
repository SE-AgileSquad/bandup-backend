using BandUp.Application.Abstractions;

namespace BandUp.Application.Features.Learning.Admin.EditLessonExample;

public sealed record EditLessonExampleCommand(Guid LessonId, Guid ExampleId, LessonExampleInput Input) : ICommand<bool>;
