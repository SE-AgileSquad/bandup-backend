using BandUp.Application.Abstractions;
using BandUp.Application.Common;
using BandUp.Domain.Exams;

namespace BandUp.Application.Features.Exams.CloseDueAttempts;

/// <summary>
/// Each due attempt is locked and read again before it is closed, so a submit that arrives at
/// the same moment either closes it first (and this one skips it) or waits and finds it already
/// closed. Closing twice is therefore impossible, and so is overwriting the idempotency key of a
/// submit that won the race. It only ever touches the caller's own attempts.
/// </summary>
public sealed class CloseDueAttemptsHandler(
    ICurrentUser currentUser,
    IAttemptRepository attempts,
    IAnswerKeyQueries answerKeyQueries,
    TimeProvider timeProvider)
    : ICommandHandler<CloseDueAttemptsCommand, int>
{
    public async Task<Result<int>> HandleAsync(CloseDueAttemptsCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId || !currentUser.IsAuthenticated)
        {
            return Result<int>.Failure(Error.Unauthorized("Sign in to access this resource."));
        }

        DateTimeOffset now = timeProvider.GetUtcNow();

        IReadOnlyList<Guid> dueIds = await attempts.ListDueAttemptIdsAsync(
            userId,
            now - Attempt.SubmissionGrace,
            command.AttemptId,
            cancellationToken);

        int closed = 0;

        foreach (Guid attemptId in dueIds)
        {
            Attempt? attempt = await attempts.GetWithAnswersForUpdateAsync(attemptId, cancellationToken);

            // Judged again under the lock: someone else may have closed it since the list was read.
            if (attempt is null || attempt.Status != AttemptStatus.InProgress || attempt.CanAcceptAnswers(now))
            {
                continue;
            }

            await AttemptClosing.CloseAsync(attempt, answerKeyQueries, now, null, cancellationToken);
            closed++;
        }

        return Result<int>.Success(closed);
    }
}
