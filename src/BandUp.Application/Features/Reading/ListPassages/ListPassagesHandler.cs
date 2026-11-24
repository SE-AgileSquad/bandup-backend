using BandUp.Application.Abstractions;
using BandUp.Application.Common;

namespace BandUp.Application.Features.Reading.ListPassages;

public sealed class ListPassagesHandler(IReadingQueries readingQueries)
    : IQueryHandler<ListPassagesQuery, PagedResult<PassageSummaryDto>>
{
    public async Task<Result<PagedResult<PassageSummaryDto>>> HandleAsync(
        ListPassagesQuery query,
        CancellationToken cancellationToken)
    {
        PagedResult<PassageSummaryDto> page = await readingQueries.ListPublishedPassagesAsync(
            new PageRequest(query.Page, query.PageSize),
            cancellationToken);

        return Result<PagedResult<PassageSummaryDto>>.Success(page);
    }
}
