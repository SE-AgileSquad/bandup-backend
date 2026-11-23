using BandUp.Domain.Content;

namespace BandUp.Domain.Exams;

/// <summary>
/// What the exam module needs to know about one question to grade it. The Application layer
/// builds these from the content module's answer keys, so this module never reads another
/// module's tables (ADR 0015).
/// </summary>
public sealed record ScoringKey(
    Guid QuestionId,
    QuestionType Type,
    string CorrectAnswer,
    IReadOnlyList<string> AcceptedVariants);
