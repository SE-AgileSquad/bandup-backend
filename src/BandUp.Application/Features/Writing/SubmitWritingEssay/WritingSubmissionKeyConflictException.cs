namespace BandUp.Application.Features.Writing.SubmitWritingEssay;

/// <summary>The acceptance save hit the owner/key constraint and the failed transaction was disposed.</summary>
public sealed class WritingSubmissionKeyConflictException(Exception innerException)
    : Exception("The Writing submission owner and idempotency key already exist.", innerException);
