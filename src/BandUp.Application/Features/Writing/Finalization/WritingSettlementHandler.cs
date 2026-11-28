using BandUp.Application.Abstractions;
using BandUp.Application.Common;
using BandUp.Application.Features.Billing;
using BandUp.Domain.Billing;
using BandUp.Domain.Writing;

namespace BandUp.Application.Features.Writing.Finalization;

/// <summary>Resolve immutable ownership without a row lock, then lock account before submission.
/// The caller's transaction commits the terminal state, feedback and financial effects together.
/// A successful false result means an identical terminal outcome was already recorded.</summary>
internal sealed class WritingSettlementHandler(IWritingSubmissionStore store, IWritingMutationWriter mutationWriter,
    ICreditLedgerRepository ledger,
    CreditService credits, TimeProvider clock) :
    CreditService.WritingSettlement(credits), ICommandHandler<CompleteWriting, bool>,
    ICommandHandler<FailWriting, bool>, ICommandHandler<ExpireWriting, bool>
{
    public Task<Result<bool>> HandleAsync(CompleteWriting command, CancellationToken ct) =>
        SettleAsync(command.SubmissionId, Outcome.Success, command.Feedback, ct);

    public Task<Result<bool>> HandleAsync(FailWriting command, CancellationToken ct) =>
        SettleAsync(command.SubmissionId, Outcome.Failure, null, ct);

    public Task<Result<bool>> HandleAsync(ExpireWriting command, CancellationToken ct) =>
        SettleAsync(command.SubmissionId, Outcome.Expiry, null, ct);

    private enum Outcome { Success, Failure, Expiry }

    private async Task<Result<bool>> SettleAsync(Guid id, Outcome outcome, WritingFeedback? feedback, CancellationToken ct)
    {
        var owner = await store.FindOwnerAsync(id, ct);
        if (owner is null)
        {
            return Result<bool>.Failure(Error.NotFound("Writing submission not found."));
        }
        var account = await LockSettlementAccountAsync(owner.Value, ct);
        var submission = await store.LockSubmissionAsync(id, ct);
        if (submission is null || submission.UserId != owner || account.UserId != owner)
        {
            return Conflict("Submission ownership does not match the locked account.");
        }

        var charge = await ledger.FindAsync(CreditLedgerEntry.WritingChargeReason, id, ct);
        if (charge is null || charge.UserId != owner || charge.Delta != -1)
        {
            return Conflict("Missing or invalid original Writing charge.");
        }
        var refund = await ledger.FindAsync(CreditLedgerEntry.WritingRefundReason, id, ct);
        var saved = await store.FindFeedbackAsync(id, ct);
        // A terminal replay has no claim on the headroom remaining for the owner's other submissions.
        if (submission.Status == WritingEvaluationStatus.Succeeded)
        {
            return outcome == Outcome.Success && saved is not null && refund is null && saved.MatchesFeedback(feedback)
                ? Result<bool>.Success(false) : Conflict("A different terminal outcome is recorded.");
        }
        if (submission.Status == WritingEvaluationStatus.Failed)
        {
            var recorded = submission.StartedAt.HasValue ? Outcome.Failure : Outcome.Expiry;
            return outcome == recorded && saved is null && refund is { Delta: 1 } && refund.UserId == owner
                ? Result<bool>.Success(false) : Conflict("A different or inconsistent terminal outcome is recorded.");
        }
        if (refund is not null || saved is not null || account.ReservedRefundCapacity < 1 ||
            account.ReservedRefundCapacity != await store.CountOutstandingAsync(owner.Value, ct))
        {
            return Conflict("An unsettled submission has inconsistent financial or feedback records.");
        }

        var now = clock.GetUtcNow();
        if (outcome == Outcome.Expiry)
        {
            if (submission.Status != WritingEvaluationStatus.Queued || now < submission.QueueDeadline)
            {
                return Conflict("Only an expired queued submission can expire.");
            }
        }
        else if (submission.Status != WritingEvaluationStatus.Processing || submission.StartedAt is null || now < submission.StartedAt)
        {
            return Conflict("Only a processing submission can be finalized.");
        }

        if (outcome == Outcome.Success)
        {
            if (feedback is null || feedback.Id != id || !feedback.HasEvidenceIn(submission.EssayText))
            {
                return Conflict("Feedback does not match this submission.");
            }
            submission.Succeed(feedback, now);
            account.ReleaseWritingRefundCapacity(now);
            await mutationWriter.AddFeedbackAsync(feedback, ct);
        }
        else
        {
            var reversal = await PostRefundAsync(account, id, ct);
            if (reversal.IsFailure)
            {
                return Result<bool>.Failure(reversal.Error!);
            }
            submission.Fail(now);
        }
        mutationWriter.AuthorizeLifecycleChange(submission);
        return Result<bool>.Success(true);
    }

    private static Result<bool> Conflict(string message) => Result<bool>.Failure(Error.Conflict(message));
}
