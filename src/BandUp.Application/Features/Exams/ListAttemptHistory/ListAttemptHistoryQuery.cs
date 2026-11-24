using BandUp.Application.Abstractions;
using BandUp.Application.Common;

namespace BandUp.Application.Features.Exams.ListAttemptHistory;

public sealed record ListAttemptHistoryQuery(int Page = 1, int PageSize = PageRequest.DefaultPageSize)
    : IQuery<PagedResult<AttemptHistoryItemDto>>;
