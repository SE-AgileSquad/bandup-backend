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
            return Result<StartAttemptResult>.Success(
                new StartAttemptResult(AttemptStateDto.From(existing, now), false));
        }

        Attempt attempt = Attempt.Start(userId, command.PassageId, now, ExamRules.AttemptDuration);
        await attempts.AddAsync(attempt, cancellationToken);

        return Result<StartAttemptResult>.Success(
            new StartAttemptResult(AttemptStateDto.From(attempt, now), true));
    }
}
