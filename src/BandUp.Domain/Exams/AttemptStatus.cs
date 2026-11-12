namespace BandUp.Domain.Exams;

/// <summary>
/// Stored as text with a CHECK constraint listing these names, like every other enum.
/// </summary>
public enum AttemptStatus
{
    InProgress,
    Submitted,
}
