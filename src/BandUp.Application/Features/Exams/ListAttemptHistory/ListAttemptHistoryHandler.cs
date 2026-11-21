using BandUp.Application.Abstractions;
using BandUp.Application.Common;

namespace BandUp.Application.Features.Exams.ListAttemptHistory;

/// <summary>
/// The signed-in learner's test history: submitted attempts, newest first. It only ever lists
/// the caller's own attempts.
/// </summary>
public sealed class ListAttemptHistoryHandler(ICurrentUser currentUser, IAttemptQueries attemptQueries)
    : IQueryHandler<ListAttemptHistoryQuery, PagedResult<AttemptHistoryItemDto>>
{
    public async Task<Result<PagedResult<AttemptHistoryItemDto>>> HandleAsync(
        ListAttemptHistoryQuery query,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId || !currentUser.IsAuthenticated)
        {
            return Result<PagedResult<AttemptHistoryItemDto>>.Failure(
                Error.Unauthorized("Sign in to access this resource."));
        }

        PagedResult<AttemptHistoryItemDto> page = await attemptQueries.ListSubmittedAsync(
            userId,
            new PageRequest(query.Page, query.PageSize),
            cancellationToken);

        return Result<PagedResult<AttemptHistoryItemDto>>.Success(page);
    }
}
