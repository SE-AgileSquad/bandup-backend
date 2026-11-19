using BandUp.Domain.Content;
using BandUp.Domain.Exams;

namespace BandUp.Application.Features.Exams;

/// <summary>
/// The outcome of a submitted attempt. The score and the band are the stored ones, never
/// recomputed, so the same attempt always shows the same result. <see cref="ApproxBand"/> is an
/// estimate and must be shown with that label. <see cref="TimeSpentSeconds"/> never exceeds
/// <see cref="DurationSeconds"/>, even when the submission arrived in the grace period.
/// </summary>
public sealed record AttemptResultDto(
    Guid AttemptId,
    Guid PassageId,
    string PassageTitle,
    int RawScore,
    int TotalQuestions,
    decimal? ApproxBand,
    DateTimeOffset SubmittedAt,
    int TimeSpentSeconds,
    int DurationSeconds,
    IReadOnlyList<QuestionResultDto> Questions)
{
    public static AttemptResultDto From(
        Attempt attempt,
        string passageTitle,
        IReadOnlyList<QuestionReview> review,
        IReadOnlyDictionary<QuestionType, Guid> lessonsByType,
        IReadOnlyDictionary<Guid, IReadOnlyList<ParaphrasePairDto>> paraphrasesByQuestion)
    {
        ArgumentNullException.ThrowIfNull(attempt);

        Dictionary<Guid, AttemptAnswer> answers = attempt.Answers.ToDictionary(answer => answer.QuestionId);

        QuestionResultDto[] questions =
        [
            .. review.Select(item =>
            {
                answers.TryGetValue(item.QuestionId, out AttemptAnswer? answer);
                bool isCorrect = answer?.IsCorrect == true;

                IReadOnlyList<ParaphrasePairDto> pairs =
                    paraphrasesByQuestion.TryGetValue(item.QuestionId, out IReadOnlyList<ParaphrasePairDto>? found)
                        ? found
                        : Array.Empty<ParaphrasePairDto>();

                return new QuestionResultDto(
                    item.QuestionId,
                    item.Type,
                    item.Prompt,
                    item.SortOrder,
                    answer?.SelectedAnswer,
                    isCorrect,
                    item.CorrectAnswer,
                    item.Explanation,
                    item.EvidenceSentence,
                    item.UnspecifiedPart,
                    !isCorrect && lessonsByType.TryGetValue(item.Type, out Guid lessonId) ? lessonId : null,
                    pairs);
            }),
        ];

        DateTimeOffset submittedAt = attempt.SubmittedAt ?? attempt.ExpiresAt;
        int durationSeconds = (int)Math.Round((attempt.ExpiresAt - attempt.StartedAt).TotalSeconds);
        int timeSpentSeconds = Math.Clamp(
            (int)Math.Round((submittedAt - attempt.StartedAt).TotalSeconds),
            0,
            durationSeconds);

        return new AttemptResultDto(
            attempt.Id,
            attempt.PassageId,
            passageTitle,
            attempt.RawScore ?? 0,
            attempt.TotalQuestions ?? 0,
            attempt.ApproxBand,
            submittedAt,
            timeSpentSeconds,
            durationSeconds,
            questions);
    }
}
