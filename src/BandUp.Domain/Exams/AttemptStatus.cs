namespace BandUp.Domain.Exams;

/// <summary>
/// Stored as text with a CHECK constraint listing these names, like every other enum.
/// </summary>
public enum AttemptStatus
{
    InProgress,
    Submitted,

    /// <summary>
    /// The deadline and the grace period passed with no answer saved, so there was nothing to grade
    /// and no result was recorded. The learner can start the passage again, which makes a new attempt.
    /// </summary>
    Expired,
}
