namespace BandUp.Application.Features.Exams;

/// <summary>
/// Where a learner stands on one passage. A passage with no entry at all is new to them.
/// </summary>
public enum PassageAttemptState
{
    /// <summary>A sitting is running and still accepts answers.</summary>
    InProgress,

    /// <summary>The learner has submitted the passage at least once.</summary>
    Completed,
}
