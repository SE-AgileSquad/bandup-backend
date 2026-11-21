using BandUp.Application.Abstractions;
using BandUp.Application.Common;
using BandUp.Application.Features.Learning;
using BandUp.Domain.Exams;

namespace BandUp.Application.Features.Exams.GetAttemptResult;

/// <summary>
/// The result of a submitted attempt, with the answer key revealed. Refused while the attempt
/// is still open, so the key can never be read mid-exam. An attempt that ran out of time has no
/// result and says so, rather than looking like one that is merely not submitted yet.
/// </summary>
public sealed class GetAttemptResultHandler(
    ICurrentUser currentUser,
    IAttemptRepository attempts,
    IAnswerKeyQueries answerKeyQueries,
    ILessonQueries lessonQueries,
    IAttemptQueries attemptQueries,
    TimeProvider timeProvider)
    : IQueryHandler<GetAttemptResultQuery, AttemptResultDto>
{
    public async Task<Result<AttemptResultDto>> HandleAsync(
        GetAttemptResultQuery query,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId || !currentUser.IsAuthenticated)
        {
            return Result<AttemptResultDto>.Failure(Error.Unauthorized("Sign in to access this resource."));
        }

        Attempt? attempt = await attempts.GetWithAnswersAsync(query.AttemptId, cancellationToken);

        if (attempt is null || attempt.UserId != userId)
        {
            return Result<AttemptResultDto>.Failure(ExamErrors.AttemptNotFound());
        }

        if (attempt.Status != AttemptStatus.Submitted)
        {
            return attempt.EffectiveStatus(timeProvider.GetUtcNow()) == AttemptStatus.Expired
                ? Result<AttemptResultDto>.Failure(ExamErrors.AttemptExpired())
                : Result<AttemptResultDto>.Failure(ExamErrors.AttemptNotSubmitted());
        }

        AttemptResultBuilder resultBuilder = new(answerKeyQueries, lessonQueries, attemptQueries);

        return Result<AttemptResultDto>.Success(await resultBuilder.BuildAsync(attempt, cancellationToken));
    }
}
