using BandUp.Application.Abstractions;
using BandUp.Application.Common;

namespace BandUp.Application.Features.Content.Admin.GetAdminPassage;

public sealed class GetAdminPassageHandler(IAdminPassageStore store)
    : IQueryHandler<GetAdminPassageQuery, PassageDetails>
{
    public async Task<Result<PassageDetails>> HandleAsync(GetAdminPassageQuery query, CancellationToken cancellationToken) =>
        await store.GetAsync(query.PassageId, cancellationToken) is { } passage
            ? Result<PassageDetails>.Success(passage)
            : Result<PassageDetails>.Failure(Error.NotFound("Passage not found."));
}
