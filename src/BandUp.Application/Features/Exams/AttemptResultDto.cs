using BandUp.Domain.Content;
using BandUp.Domain.Exams;

namespace BandUp.Application.Features.Exams;

/// <summary>
/// The outcome of a submitted attempt. The score is the stored one, never recomputed, so the
/// same attempt always shows the same result.
/// </summary>
public sealed record AttemptResultDto(
    Guid AttemptId,
    Guid PassageId,
    int RawScore,
    int TotalQuestions,
    DateTimeOffset SubmittedAt,
    IReadOnlyList<QuestionResultDto> Questions)
{
    public static AttemptResultDto From(
        Attempt attempt,
        IReadOnlyList<QuestionReview> review,
        IReadOnlyDictionary<QuestionType, Guid> lessonsByType)
    {
        ArgumentNullException.ThrowIfNull(attempt);

        Dictionary<Guid, AttemptAnswer> answers = attempt.Answers.ToDictionary(answer => answer.QuestionId);

        QuestionResultDto[] questions =
        [
            .. review.Select(item =>
            {
                answers.TryGetValue(item.QuestionId, out AttemptAnswer? answer);
                bool isCorrect = answer?.IsCorrect == true;

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
                    !isCorrect && lessonsByType.TryGetValue(item.Type, out Guid lessonId) ? lessonId : null);
            }),
        ];

        return new AttemptResultDto(
            attempt.Id,
            attempt.PassageId,
            attempt.RawScore ?? 0,
            attempt.TotalQuestions ?? 0,
            attempt.SubmittedAt ?? attempt.ExpiresAt,
            questions);
    }
}
