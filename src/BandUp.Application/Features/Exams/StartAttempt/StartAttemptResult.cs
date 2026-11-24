namespace BandUp.Application.Features.Exams.StartAttempt;

/// <summary>
/// <see cref="Created"/> is false when the learner already had this passage in progress and
/// the existing attempt is handed back, which is how a refresh or a second tab recovers.
/// </summary>
public sealed record StartAttemptResult(AttemptStateDto Attempt, bool Created);
