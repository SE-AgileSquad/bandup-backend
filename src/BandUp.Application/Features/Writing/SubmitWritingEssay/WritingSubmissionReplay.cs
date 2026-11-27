using BandUp.Application.Common;
using BandUp.Domain.Writing;

namespace BandUp.Application.Features.Writing.SubmitWritingEssay;

public static class WritingSubmissionReplay
{
    public static Result<WritingSubmissionAcceptedDto> Compare(WritingSubmission submission, SubmitWritingEssayCommand command) =>
        submission.MatchesPayload(command.PromptId, command.EssayText)
            ? Receipt(submission)
            : Result<WritingSubmissionAcceptedDto>.Failure(new Error(WritingErrorCodes.IdempotencyConflict,
                "The idempotency key was accepted with a different payload.", ErrorType.Conflict));

    internal static Result<WritingSubmissionAcceptedDto> Receipt(WritingSubmission submission) =>
        Result<WritingSubmissionAcceptedDto>.Success(new(submission.Id, submission.SubmittedAt));
}
