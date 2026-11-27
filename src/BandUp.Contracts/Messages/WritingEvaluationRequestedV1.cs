namespace BandUp.Contracts.Messages;

/// <summary>Identifies the accepted submission. The consumer reads its input from persistence.</summary>
public sealed record WritingEvaluationRequestedV1(Guid SubmissionId);
