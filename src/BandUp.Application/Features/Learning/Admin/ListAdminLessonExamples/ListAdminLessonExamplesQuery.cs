using BandUp.Application.Abstractions;

namespace BandUp.Application.Features.Learning.Admin.ListAdminLessonExamples;

public sealed record ListAdminLessonExamplesQuery(Guid LessonId) : IQuery<IReadOnlyList<LessonExampleDetails>>;
