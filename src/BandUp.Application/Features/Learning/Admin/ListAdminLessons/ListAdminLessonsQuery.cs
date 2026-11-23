using BandUp.Application.Abstractions;
using BandUp.Application.Common;
using BandUp.Domain.Learning;

namespace BandUp.Application.Features.Learning.Admin.ListAdminLessons;

public sealed record ListAdminLessonsQuery(PageRequest Page, LessonStatus? Status = null, string? Search = null)
    : IQuery<PagedResult<LessonSummary>>;
