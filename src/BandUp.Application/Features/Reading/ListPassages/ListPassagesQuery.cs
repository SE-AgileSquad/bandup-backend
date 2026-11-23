using BandUp.Application.Abstractions;
using BandUp.Application.Common;

namespace BandUp.Application.Features.Reading.ListPassages;

public sealed record ListPassagesQuery(int Page = 1, int PageSize = PageRequest.DefaultPageSize)
    : IQuery<PagedResult<PassageSummaryDto>>;
