using BandUp.Application.Abstractions;
using BandUp.Application.Common;

namespace BandUp.Application.Features.Writing;

public sealed record ListWritingPromptsQuery : IQuery<WritingPromptListDto>;

internal sealed class ListWritingPromptsHandler(IWritingSubmissionStore store)
    : IQueryHandler<ListWritingPromptsQuery, WritingPromptListDto>
{
    public async Task<Result<WritingPromptListDto>> HandleAsync(ListWritingPromptsQuery query, CancellationToken cancellationToken) =>
        Result<WritingPromptListDto>.Success(await store.ListEligiblePromptsAsync(cancellationToken));
}
