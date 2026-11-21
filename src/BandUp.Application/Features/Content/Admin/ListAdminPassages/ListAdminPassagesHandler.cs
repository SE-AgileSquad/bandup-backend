using BandUp.Application.Abstractions;
using BandUp.Application.Common;

namespace BandUp.Application.Features.Content.Admin.ListAdminPassages;

public sealed class ListAdminPassagesHandler(IAdminPassageStore store)
    : IQueryHandler<ListAdminPassagesQuery, PagedResult<PassageSummary>>
{
    public async Task<Result<PagedResult<PassageSummary>>> HandleAsync(
        ListAdminPassagesQuery query, CancellationToken cancellationToken) =>
        Result<PagedResult<PassageSummary>>.Success(await store.ListAsync(query.Page, cancellationToken));
}
