namespace BandUp.Application.Features.Exams;

/// <summary>
/// One row of the learner's test history: a submitted attempt with its score. Attempts that ran
/// out of time record no result and never appear here.
/// </summary>
public sealed record AttemptHistoryItemDto(
    Guid AttemptId,
    Guid PassageId,
    string PassageTitle,
    DateTimeOffset SubmittedAt,
    int RawScore,
    int TotalQuestions,
    decimal? ApproxBand);
