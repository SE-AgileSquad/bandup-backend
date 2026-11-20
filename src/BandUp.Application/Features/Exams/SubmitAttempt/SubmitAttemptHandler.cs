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
/// </summary>
public sealed class SubmitAttemptHandler(
    ICurrentUser currentUser,
    IAttemptRepository attempts,
    IAnswerKeyQueries answerKeyQueries,
    ILessonQueries lessonQueries,
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

        AttemptResultBuilder resultBuilder = new(answerKeyQueries, lessonQueries);

        if (attempt.Status == AttemptStatus.Submitted)
        {
            return attempt.IsReplayOf(command.IdempotencyKey)
                ? Result<AttemptResultDto>.Success(await resultBuilder.BuildAsync(attempt, cancellationToken))
                : Result<AttemptResultDto>.Failure(ExamErrors.AttemptAlreadySubmitted());
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

        attempt.Submit(finalAnswers, keys, command.IdempotencyKey, timeProvider.GetUtcNow());

        return Result<AttemptResultDto>.Success(await resultBuilder.BuildAsync(attempt, cancellationToken));
    }
}
