using BandUp.Application.Abstractions;
using BandUp.Application.Common;

namespace BandUp.Application.Features.Exams.GetPassageAttemptSummaries;

/// <summary>
/// Where the signed-in learner stands on each passage, so the Reading Tests list can show
/// New (no entry), In progress with the time left, or Completed with the last score. The time
/// left is judged by the server clock at this moment.
/// </summary>
public sealed class GetPassageAttemptSummariesHandler(
    ICurrentUser currentUser,
    IAttemptQueries attemptQueries,
    TimeProvider timeProvider)
    : IQueryHandler<GetPassageAttemptSummariesQuery, IReadOnlyList<PassageAttemptSummaryDto>>
{
    public async Task<Result<IReadOnlyList<PassageAttemptSummaryDto>>> HandleAsync(
        GetPassageAttemptSummariesQuery query,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId || !currentUser.IsAuthenticated)
        {
            return Result<IReadOnlyList<PassageAttemptSummaryDto>>.Failure(
                Error.Unauthorized("Sign in to access this resource."));
        }

        IReadOnlyList<PassageAttemptSummaryDto> summaries = await attemptQueries.GetPassageSummariesAsync(
            userId,
            timeProvider.GetUtcNow(),
            cancellationToken);

        return Result<IReadOnlyList<PassageAttemptSummaryDto>>.Success(summaries);
    }
}
