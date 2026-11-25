using BandUp.Application.Abstractions;
using BandUp.Application.Common;

namespace BandUp.Application.Features.Writing.Admin.GetAdminWritingPrompt;

public sealed class GetAdminWritingPromptHandler(IAdminWritingPromptStore store)
    : IQueryHandler<GetAdminWritingPromptQuery, WritingPromptDetails>
{
    public async Task<Result<WritingPromptDetails>> HandleAsync(
        GetAdminWritingPromptQuery query, CancellationToken cancellationToken) =>
        await store.GetAsync(query.PromptId, cancellationToken) is { } prompt
            ? Result<WritingPromptDetails>.Success(prompt)
            : Result<WritingPromptDetails>.Failure(Error.NotFound("Writing prompt not found."));
}

