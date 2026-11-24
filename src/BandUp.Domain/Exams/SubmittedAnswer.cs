namespace BandUp.Domain.Exams;

/// <summary>
/// One answer the learner sends with the final submission.
/// </summary>
public sealed record SubmittedAnswer(Guid QuestionId, string SelectedAnswer);
