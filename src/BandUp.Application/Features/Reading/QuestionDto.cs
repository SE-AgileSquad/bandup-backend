using BandUp.Domain.Content;

namespace BandUp.Application.Features.Reading;

/// <summary>
/// A question as a learner may see it. It deliberately has no answer, explanation, evidence
/// sentence, misleading sentence or unspecified part: those are revealed only by the result of
/// a submitted attempt. <see cref="RelatedLessonId"/> is the lesson mapped to the question type.
/// </summary>
public sealed record QuestionDto(
    Guid Id,
    QuestionType Type,
    string Prompt,
    int SortOrder,
    Guid? RelatedLessonId);
