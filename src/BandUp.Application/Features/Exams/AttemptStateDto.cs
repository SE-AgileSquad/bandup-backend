using BandUp.Domain.Exams;

namespace BandUp.Application.Features.Exams;

/// <summary>
/// What the browser needs to draw the exam after a refresh: the deadline, the time left as the
/// server sees it right now, and the answers already saved. The questions themselves come from
/// the reading endpoint for <see cref="PassageId"/>.
/// </summary>
public sealed record AttemptStateDto(
    Guid Id,
    Guid PassageId,
    AttemptStatus Status,
    DateTimeOffset StartedAt,
    DateTimeOffset ExpiresAt,
    DateTimeOffset ServerNow,
    int RemainingSeconds,
    bool IsExpired,
    IReadOnlyList<AttemptAnswerDto> Answers)
{
    public static AttemptStateDto From(Attempt attempt, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(attempt);

        return new AttemptStateDto(
            attempt.Id,
            attempt.PassageId,
            attempt.Status,
            attempt.StartedAt,
            attempt.ExpiresAt,
            now,
            (int)Math.Ceiling(attempt.RemainingAt(now).TotalSeconds),
            attempt.IsExpired(now),
            [.. attempt.Answers.Select(answer => new AttemptAnswerDto(answer.QuestionId, answer.SelectedAnswer))]);
    }
}
