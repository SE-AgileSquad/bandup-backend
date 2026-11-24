using BandUp.Domain.Exams;

namespace BandUp.Application.Features.Exams;

/// <summary>
/// How a sitting whose deadline and grace have passed is closed, in one place so every path
/// that notices it (a late submit, starting the passage again, reading the attempt or the
/// lists) does the same thing. Answers already saved are graded as if submitted at the
/// deadline; a sitting with nothing saved has nothing to grade and is closed as expired.
/// </summary>
internal static class AttemptClosing
{
    public static async Task CloseAsync(
        Attempt attempt,
        IAnswerKeyQueries answerKeyQueries,
        DateTimeOffset now,
        string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        if (attempt.Answers.Count == 0)
        {
            attempt.Expire(now);
            return;
        }

        IReadOnlyList<ScoringKey> keys =
            await answerKeyQueries.GetScoringKeysAsync(attempt.PassageId, cancellationToken);

        attempt.SubmitAtDeadline(keys, now, idempotencyKey);
    }
}
