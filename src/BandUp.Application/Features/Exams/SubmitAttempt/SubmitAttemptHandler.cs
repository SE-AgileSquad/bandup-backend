using BandUp.Application.Abstractions;
using BandUp.Application.Common;
using BandUp.Application.Features.Learning;
using BandUp.Domain.Exams;

namespace BandUp.Application.Features.Exams.SubmitAttempt;

/// <summary>
/// Submits an attempt exactly once. The attempt row is locked first, so concurrent submits
/// take turns: the first grades it, the others find it already submitted and either replay the
/// stored result (same idempotency key) or are refused (a different key). Grading happens only
/// inside the one transition the aggregate allows, so no sequence of retries can score twice.
/// A submission that arrives after the deadline and grace is too late: the answers in it are
/// ignored and the sitting is graded from the answers already saved, as of the deadline. With
/// nothing saved there is nothing to grade, and the submission is refused as expired.
/// </summary>
public sealed class SubmitAttemptHandler(
    ICurrentUser currentUser,
    IAttemptRepository attempts,
    IAnswerKeyQueries answerKeyQueries,
    ILessonQueries lessonQueries,
    IAttemptQueries attemptQueries,
    TimeProvider timeProvider)
    : ICommandHandler<SubmitAttemptCommand, AttemptResultDto>
{
    public async Task<Result<AttemptResultDto>> HandleAsync(
        SubmitAttemptCommand command,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId || !currentUser.IsAuthenticated)
        {
            return Result<AttemptResultDto>.Failure(Error.Unauthorized("Sign in to access this resource."));
        }

        Attempt? attempt = await attempts.GetWithAnswersForUpdateAsync(command.AttemptId, cancellationToken);

        if (attempt is null || attempt.UserId != userId)
        {
            return Result<AttemptResultDto>.Failure(ExamErrors.AttemptNotFound());
        }

        AttemptResultBuilder resultBuilder = new(answerKeyQueries, lessonQueries, attemptQueries);

        if (attempt.Status == AttemptStatus.Submitted)
        {
            // A sitting the server already closed at its deadline answers any submit with that
            // same result, so a slow request that loses the race to a refresh still gets it.
            return attempt.IsReplayOf(command.IdempotencyKey) || attempt.WasClosedByServer
                ? Result<AttemptResultDto>.Success(await resultBuilder.BuildAsync(attempt, cancellationToken))
                : Result<AttemptResultDto>.Failure(ExamErrors.AttemptAlreadySubmitted());
        }

        DateTimeOffset now = timeProvider.GetUtcNow();

        // Past the deadline plus grace this request is too late, and so are the answers in it.
        // What the learner saved before the deadline is graded instead; with nothing saved (or an
        // attempt already closed as expired) there is no result.
        if (!attempt.CanAcceptAnswers(now))
        {
            if (attempt.Status != AttemptStatus.InProgress || attempt.Answers.Count == 0)
            {
                return Result<AttemptResultDto>.Failure(ExamErrors.AttemptExpired());
            }

            if (await attempts.IdempotencyKeyUsedAsync(userId, command.IdempotencyKey, cancellationToken))
            {
                return Result<AttemptResultDto>.Failure(ExamErrors.IdempotencyKeyReused());
            }

            await AttemptClosing.CloseAsync(
                attempt,
                answerKeyQueries,
                now,
                command.IdempotencyKey,
                cancellationToken);

            return Result<AttemptResultDto>.Success(await resultBuilder.BuildAsync(attempt, cancellationToken));
        }

        // The attempt is still open, so any attempt already holding this key is another one.
        if (await attempts.IdempotencyKeyUsedAsync(userId, command.IdempotencyKey, cancellationToken))
        {
            return Result<AttemptResultDto>.Failure(ExamErrors.IdempotencyKeyReused());
        }

        IReadOnlyList<ScoringKey> keys =
            await answerKeyQueries.GetScoringKeysAsync(attempt.PassageId, cancellationToken);

        HashSet<Guid> questionIds = [.. keys.Select(key => key.QuestionId)];

        if (command.Answers.Any(answer => !questionIds.Contains(answer.QuestionId)))
        {
            return Result<AttemptResultDto>.Failure(ExamErrors.QuestionNotInAttempt());
        }

        SubmittedAnswer[] finalAnswers =
        [
            .. command.Answers.Select(
                answer => new SubmittedAnswer(answer.QuestionId, answer.SelectedAnswer ?? string.Empty)),
        ];

        attempt.Submit(finalAnswers, keys, command.IdempotencyKey, now);

        return Result<AttemptResultDto>.Success(await resultBuilder.BuildAsync(attempt, cancellationToken));
    }
}
