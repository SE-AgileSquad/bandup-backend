using BandUp.Application.Abstractions;
using BandUp.Application.Common;
using BandUp.Application.Features.Reading;
using BandUp.Domain.Exams;

namespace BandUp.Application.Features.Exams.StartAttempt;

/// <summary>
/// Starts an attempt, or returns the one already running. The lock makes two simultaneous
/// starts (a double click, two tabs) take turns, so exactly one creates the attempt and the
/// other finds it; the partial unique index on the table is only the last line of defence.
/// </summary>
public sealed class StartAttemptHandler(
    ICurrentUser currentUser,
    IAttemptRepository attempts,
    IReadingQueries readingQueries,
    IAnswerKeyQueries answerKeyQueries,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider)
    : ICommandHandler<StartAttemptCommand, StartAttemptResult>
{
    public async Task<Result<StartAttemptResult>> HandleAsync(
        StartAttemptCommand command,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId || !currentUser.IsAuthenticated)
        {
            return Result<StartAttemptResult>.Failure(Error.Unauthorized("Sign in to access this resource."));
        }

        PassageDto? passage = await readingQueries.GetPublishedPassageAsync(command.PassageId, cancellationToken);

        if (passage is null)
        {
            return Result<StartAttemptResult>.Failure(
                Error.NotFound("The passage does not exist or is not available."));
        }

        await attempts.AcquireStartLockAsync(userId, command.PassageId, cancellationToken);

        DateTimeOffset now = timeProvider.GetUtcNow();
        Attempt? existing = await attempts.FindInProgressAsync(userId, command.PassageId, cancellationToken);

        if (existing is not null)
        {
            if (existing.CanAcceptAnswers(now))
            {
                return Result<StartAttemptResult>.Success(
                    new StartAttemptResult(AttemptStateDto.From(existing, now), false));
            }

            // The old attempt ran out of time and was never submitted. Close it with the answers
            // it holds, and save that before inserting the new one: the unique index allows only
            // one in-progress attempt, so the update must reach the database first.
            IReadOnlyList<ScoringKey> keys =
                await answerKeyQueries.GetScoringKeysAsync(command.PassageId, cancellationToken);

            existing.Submit([], keys, $"auto-closed-{existing.Id:N}", now);
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        Attempt attempt = Attempt.Start(userId, command.PassageId, now, ExamRules.AttemptDuration);
        await attempts.AddAsync(attempt, cancellationToken);

        return Result<StartAttemptResult>.Success(
            new StartAttemptResult(AttemptStateDto.From(attempt, now), true));
    }
}
