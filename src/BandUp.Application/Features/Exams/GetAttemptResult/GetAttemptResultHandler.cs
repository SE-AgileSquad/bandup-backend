using BandUp.Application.Abstractions;
using BandUp.Application.Common;
using BandUp.Application.Features.Learning;
using BandUp.Domain.Exams;

namespace BandUp.Application.Features.Exams.GetAttemptResult;

/// <summary>
/// The result of a submitted attempt, with the answer key revealed. Refused while the attempt
/// is still open, so the key can never be read mid-exam.
/// </summary>
public sealed class GetAttemptResultHandler(
    ICurrentUser currentUser,
    IAttemptRepository attempts,
    IAnswerKeyQueries answerKeyQueries,
    ILessonQueries lessonQueries)
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
            return Result<AttemptResultDto>.Failure(ExamErrors.AttemptNotSubmitted());
        }

        AttemptResultBuilder resultBuilder = new(answerKeyQueries, lessonQueries);

        return Result<AttemptResultDto>.Success(await resultBuilder.BuildAsync(attempt, cancellationToken));
    }
}
