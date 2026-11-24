using BandUp.Domain.Content;

namespace BandUp.Application.Features.Exams;

/// <summary>
/// One question of a submitted attempt, with the answer key revealed. <see cref="RelatedLessonId"/>
/// is set only for a question answered wrongly or not at all. <see cref="ParaphrasePairs"/> are the
/// approved pairs for the question (empty when it has none), so the review can show how the
/// question's wording maps onto the passage.
/// </summary>
public sealed record QuestionResultDto(
    Guid QuestionId,
    QuestionType Type,
    string Prompt,
    int SortOrder,
    string? SelectedAnswer,
    bool IsCorrect,
    string CorrectAnswer,
    string? Explanation,
    string? EvidenceSentence,
    string? UnspecifiedPart,
    Guid? RelatedLessonId,
    IReadOnlyList<ParaphrasePairDto> ParaphrasePairs);
