using BandUp.Application.Abstractions;
using BandUp.Application.Common;
using BandUp.Domain.Writing;

namespace BandUp.Application.Features.Writing.Finalization;

/// <summary>A claim owns only the submission lock. It never acquires a financial lock.</summary>
internal sealed class ClaimWritingHandler(IWritingSubmissionStore store, IWritingMutationWriter mutationWriter, TimeProvider clock) : ICommandHandler<ClaimWriting, bool>
{
    public async Task<Result<bool>> HandleAsync(ClaimWriting command, CancellationToken ct)
    {
        var submission = await store.LockSubmissionAsync(command.SubmissionId, ct);
        if (submission is null)
        {
            return Result<bool>.Failure(Error.NotFound("Writing submission not found."));
        }
        var now = clock.GetUtcNow();
        if (submission.Status != WritingEvaluationStatus.Queued || now < submission.SubmittedAt || now >= submission.QueueDeadline)
        {
            return Result<bool>.Failure(Error.Conflict("Submission is not claimable."));
        }
        submission.StartProcessing(now);
        mutationWriter.AuthorizeLifecycleChange(submission);
        return Result<bool>.Success(true);
    }
}
