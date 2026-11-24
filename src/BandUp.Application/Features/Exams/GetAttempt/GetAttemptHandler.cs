using BandUp.Application.Abstractions;
using BandUp.Application.Common;
using BandUp.Domain.Exams;

namespace BandUp.Application.Features.Exams.GetAttempt;

/// <summary>
/// The state of one attempt, with the time left computed from the server clock at this moment.
/// Someone else's attempt looks exactly like a missing one.
/// </summary>
public sealed class GetAttemptHandler(
    ICurrentUser currentUser,
    IAttemptRepository attempts,
    TimeProvider timeProvider)
    : IQueryHandler<GetAttemptQuery, AttemptStateDto>
{
    public async Task<Result<AttemptStateDto>> HandleAsync(
        GetAttemptQuery query,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId || !currentUser.IsAuthenticated)
        {
            return Result<AttemptStateDto>.Failure(Error.Unauthorized("Sign in to access this resource."));
        }

        Attempt? attempt = await attempts.GetWithAnswersAsync(query.AttemptId, cancellationToken);

        if (attempt is null || attempt.UserId != userId)
        {
            return Result<AttemptStateDto>.Failure(ExamErrors.AttemptNotFound());
        }

        return Result<AttemptStateDto>.Success(AttemptStateDto.From(attempt, timeProvider.GetUtcNow()));
    }
}
