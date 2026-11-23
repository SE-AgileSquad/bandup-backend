using BandUp.Application.Abstractions;

namespace BandUp.Application.Features.Learning.Admin.GetAdminLesson;

public sealed record GetAdminLessonQuery(Guid LessonId) : IQuery<LessonDetails>;
