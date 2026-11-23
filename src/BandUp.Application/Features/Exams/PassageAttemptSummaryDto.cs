namespace BandUp.Application.Features.Exams;

/// <summary>
/// The learner's current standing on one passage, for the Reading Tests list. For
/// <see cref="PassageAttemptState.InProgress"/> the attempt is the running one and
/// <see cref="RemainingSeconds"/> is set; for <see cref="PassageAttemptState.Completed"/> it is the
/// most recent submitted one and the score fields are set. A passage the learner has not started,
/// or whose only sitting ran out of time, has no entry.
/// </summary>
public sealed record PassageAttemptSummaryDto(
    Guid PassageId,
    PassageAttemptState State,
    Guid AttemptId,
    int? RemainingSeconds,
    int? RawScore,
    int? TotalQuestions,
    decimal? ApproxBand,
    DateTimeOffset? SubmittedAt);
