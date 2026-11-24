using BandUp.Application.Abstractions;
using BandUp.Application.Common;

namespace BandUp.Application.Features.Content.Admin.ListAdminPassages;

public sealed record ListAdminPassagesQuery(PageRequest Page) : IQuery<PagedResult<PassageSummary>>;
