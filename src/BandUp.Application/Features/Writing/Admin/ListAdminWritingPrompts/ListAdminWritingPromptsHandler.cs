using BandUp.Application.Abstractions;
using BandUp.Application.Common;

namespace BandUp.Application.Features.Writing.Admin.ListAdminWritingPrompts;

public sealed class ListAdminWritingPromptsHandler(IAdminWritingPromptStore store)
    : IQueryHandler<ListAdminWritingPromptsQuery, PagedResult<WritingPromptSummary>>
{
    public async Task<Result<PagedResult<WritingPromptSummary>>> HandleAsync(
        ListAdminWritingPromptsQuery query, CancellationToken cancellationToken) =>
        Result<PagedResult<WritingPromptSummary>>.Success(
            await store.ListAsync(query.Page, query.Status, query.Search?.Trim(), cancellationToken));
}

