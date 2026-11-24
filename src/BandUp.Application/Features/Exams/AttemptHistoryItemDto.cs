namespace BandUp.Application.Features.Exams;

/// <summary>
/// One row of the learner's test history: a submitted attempt with its score. A sitting that ran
/// out of time appears here when its saved answers were graded at the deadline; one with nothing
/// saved records no result and never appears.
/// </summary>
public sealed record AttemptHistoryItemDto(
    Guid AttemptId,
    Guid PassageId,
    string PassageTitle,
    DateTimeOffset SubmittedAt,
    int RawScore,
    int TotalQuestions,
    decimal? ApproxBand);
