using BandUp.Application.Abstractions;

namespace BandUp.Application.Features.Learning.GetLesson;

public sealed record GetLessonQuery(Guid LessonId) : IQuery<LessonDto>;
