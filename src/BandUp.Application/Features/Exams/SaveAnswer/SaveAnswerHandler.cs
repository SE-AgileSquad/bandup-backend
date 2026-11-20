using BandUp.Application.Abstractions;
using BandUp.Application.Common;
using BandUp.Application.Features.Reading;
using BandUp.Domain.Exams;

namespace BandUp.Application.Features.Exams.SaveAnswer;

/// <summary>
/// The expected refusals (not yours, already submitted, out of time, wrong question) are
/// returned here; the aggregate's own exceptions stay for programming errors. The attempt row
/// is locked while it is changed, so an autosave and a submit cannot interleave.
/// </summary>
public sealed class SaveAnswerHandler(
    ICurrentUser currentUser,
    IAttemptRepository attempts,
    IReadingQueries readingQueries,
    TimeProvider timeProvider)
    : ICommandHandler<SaveAnswerCommand, AttemptStateDto>
{
    public async Task<Result<AttemptStateDto>> HandleAsync(
        SaveAnswerCommand command,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId || !currentUser.IsAuthenticated)
        {
            return Result<AttemptStateDto>.Failure(Error.Unauthorized("Sign in to access this resource."));
        }

        Attempt? attempt = await attempts.GetWithAnswersForUpdateAsync(command.AttemptId, cancellationToken);

        if (attempt is null || attempt.UserId != userId)
        {
            return Result<AttemptStateDto>.Failure(ExamErrors.AttemptNotFound());
        }

        if (attempt.Status == AttemptStatus.Submitted)
        {
            return Result<AttemptStateDto>.Failure(ExamErrors.AttemptAlreadySubmitted());
        }

        DateTimeOffset now = timeProvider.GetUtcNow();

        if (!attempt.CanAcceptAnswers(now))
        {
            return Result<AttemptStateDto>.Failure(ExamErrors.AttemptExpired());
        }

        IReadOnlyList<Guid> questionIds =
            await readingQueries.GetQuestionIdsAsync(attempt.PassageId, cancellationToken);

        if (!questionIds.Contains(command.QuestionId))
        {
            return Result<AttemptStateDto>.Failure(ExamErrors.QuestionNotInAttempt());
        }

        attempt.SaveAnswer(command.QuestionId, command.SelectedAnswer, now);

        return Result<AttemptStateDto>.Success(AttemptStateDto.From(attempt, now));
    }
}
