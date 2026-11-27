using BandUp.Application.Abstractions;
using BandUp.Application.Common;
using BandUp.Domain.Content;

namespace BandUp.Application.Features.Content.Admin.ListAdminPassages;

public sealed record ListAdminPassagesQuery(PageRequest Page, PassageStatus? Status = null, string? Search = null)
    : IQuery<PagedResult<PassageSummary>>;
