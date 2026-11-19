namespace BandUp.Domain.Exams;

/// <summary>
/// Stored as text with a CHECK constraint listing these names, like every other enum.
/// </summary>
public enum AttemptStatus
{
    InProgress,
    Submitted,

    /// <summary>
    /// The deadline and the grace period passed before the attempt was submitted, so no result
    /// was recorded. The learner can start the passage again, which makes a new attempt.
    /// </summary>
    Expired,
}
