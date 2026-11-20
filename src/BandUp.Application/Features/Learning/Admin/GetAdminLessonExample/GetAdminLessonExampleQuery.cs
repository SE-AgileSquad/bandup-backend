using BandUp.Application.Abstractions;

namespace BandUp.Application.Features.Learning.Admin.GetAdminLessonExample;

public sealed record GetAdminLessonExampleQuery(Guid LessonId, Guid ExampleId) : IQuery<LessonExampleDetails>;
