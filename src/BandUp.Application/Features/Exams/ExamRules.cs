namespace BandUp.Application.Features.Exams;

/// <summary>
/// Product rules for an exam sitting that are not properties of a single attempt.
/// </summary>
public static class ExamRules
{
    /// <summary>
    /// One third of the 60-minute IELTS Reading paper, for a single passage. Fixed into the
    /// attempt when it starts, so changing this later never shortens or extends a running exam.
    /// </summary>
    public static readonly TimeSpan AttemptDuration = TimeSpan.FromMinutes(20);
}
