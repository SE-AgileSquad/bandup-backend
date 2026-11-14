using BandUp.Application.Abstractions;
using BandUp.Application.Common;

namespace BandUp.Application.Features.Learning.Admin.ListAdminLessons;

public sealed record ListAdminLessonsQuery(PageRequest Page) : IQuery<PagedResult<LessonSummary>>;
