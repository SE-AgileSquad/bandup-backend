using BandUp.Application.Abstractions;

namespace BandUp.Application.Features.Learning.Admin.CreateLessonExample;

public sealed record CreateLessonExampleCommand(Guid LessonId, LessonExampleInput Input) : ICommand<Guid>;
