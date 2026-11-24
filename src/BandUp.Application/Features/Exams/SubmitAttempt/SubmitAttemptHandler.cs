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
/// A submission that arrives after the deadline and grace is refused as expired and records no
/// result.
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
            return attempt.IsReplayOf(command.IdempotencyKey)
                ? Result<AttemptResultDto>.Success(await resultBuilder.BuildAsync(attempt, cancellationToken))
                : Result<AttemptResultDto>.Failure(ExamErrors.AttemptAlreadySubmitted());
        }

        DateTimeOffset now = timeProvider.GetUtcNow();

        // Past the deadline plus grace the sitting is over. Nothing is graded and no result is
        // recorded; this also covers an attempt already closed as expired.
        if (!attempt.CanAcceptAnswers(now))
        {
            return Result<AttemptResultDto>.Failure(ExamErrors.AttemptExpired());
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
