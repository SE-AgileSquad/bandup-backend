using BandUp.Domain.Content;
using BandUp.Domain.Exams;

namespace BandUp.Application.Features.Exams;

/// <summary>
/// What the exam module needs from the content module's answer keys. This is the only way the
/// keys leave the content tables, and it is used only by grading and by the result of a
/// submitted attempt; nothing a learner can call mid-exam goes through it.
/// </summary>
public interface IAnswerKeyQueries
{
    /// <summary>
    /// A key for each published question of the passage that has an answer key.
    /// </summary>
    Task<IReadOnlyList<ScoringKey>> GetScoringKeysAsync(Guid passageId, CancellationToken cancellationToken);

    /// <summary>
    /// The same questions with the material shown after an exam: correct answer, explanation and
    /// evidence. Ordered by question order.
    /// </summary>
    Task<IReadOnlyList<QuestionReview>> GetReviewAsync(Guid passageId, CancellationToken cancellationToken);
}

public sealed record QuestionReview(
    Guid QuestionId,
    QuestionType Type,
    string Prompt,
    int SortOrder,
    string CorrectAnswer,
    string? Explanation,
    string? EvidenceSentence,
    string? UnspecifiedPart);
