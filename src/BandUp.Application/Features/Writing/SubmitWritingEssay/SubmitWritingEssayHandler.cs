using BandUp.Application.Abstractions;
using BandUp.Application.Common;
using BandUp.Application.Features.Billing;
using BandUp.Domain.Writing;

namespace BandUp.Application.Features.Writing.SubmitWritingEssay;

/// <summary>The command transaction owns both locks and commits the snapshot, debit and reservation together.</summary>
internal sealed class SubmitWritingEssayHandler(ICurrentUser currentUser, IWritingSubmissionStore store,
    CreditService credits, TimeProvider clock, WritingAcceptanceSettings settings, IWritingMutationWriter mutationWriter)
    : CreditService.WritingAcceptance(credits), ICommandHandler<SubmitWritingEssayCommand, WritingSubmissionAcceptedDto>
{
    public async Task<Result<WritingSubmissionAcceptedDto>> HandleAsync(SubmitWritingEssayCommand command, CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId is not { } owner || owner == Guid.Empty)
        {
            return Result<WritingSubmissionAcceptedDto>.Failure(Error.Unauthorized("Sign in to submit an essay."));
        }

        var existing = await store.FindByKeyAsync(owner, command.IdempotencyKey, cancellationToken);
        if (existing is not null)
        {
            return Replay(existing, command);
        }

        // At Read Committed a waiter sees the winning command only after acquiring the account lock.
        var account = await LockAcceptanceAccountAsync(owner, cancellationToken);
        existing = await store.FindByKeyAsync(owner, command.IdempotencyKey, cancellationToken);
        if (existing is not null)
        {
            return Replay(existing, command);
        }

        if (!EssayPolicy.IsValid(command.EssayText))
        {
            return Result<WritingSubmissionAcceptedDto>.Failure(Error.Validation(new Dictionary<string, string[]>
            {
                [nameof(command.EssayText)] = ["An essay must contain 100 to 350 words and fit the character limit."],
            }));
        }

        var prompt = await store.FindEligiblePromptAsync(command.PromptId, cancellationToken);
        var now = clock.GetUtcNow();
        if (prompt is null || now < prompt.UpdatedAt)
        {
            return Result<WritingSubmissionAcceptedDto>.Failure(Error.NotFound("The writing prompt is unavailable."));
        }

        if (account.ReservedRefundCapacity != await store.CountOutstandingAsync(owner, cancellationToken))
        {
            return Result<WritingSubmissionAcceptedDto>.Failure(Error.Conflict("Writing refund reservations do not match outstanding submissions."));
        }

        // Match PostgreSQL microsecond precision so a replay reproduces even the first receipt's timestamp.
        var acceptedAt = now.AddTicks(-(now.Ticks % 10));
        var submission = new WritingSubmission(owner, command.IdempotencyKey, prompt, command.EssayText,
            acceptedAt, acceptedAt.Add(settings.QueueTimeout));
        var debit = await PostChargeAsync(account, submission.Id, cancellationToken);
        if (debit.IsFailure)
        {
            return Result<WritingSubmissionAcceptedDto>.Failure(debit.Error!);
        }
        await mutationWriter.AddAcceptedSubmissionAsync(submission, cancellationToken);
        return Receipt(submission);
    }

    private static Result<WritingSubmissionAcceptedDto> Replay(WritingSubmission submission, SubmitWritingEssayCommand command) =>
        submission.MatchesPayload(command.PromptId, command.EssayText)
            ? Receipt(submission)
            : Result<WritingSubmissionAcceptedDto>.Failure(new Error(WritingErrorCodes.IdempotencyConflict,
                "The idempotency key was accepted with a different payload.", ErrorType.Conflict));

    private static Result<WritingSubmissionAcceptedDto> Receipt(WritingSubmission submission) =>
        Result<WritingSubmissionAcceptedDto>.Success(new(submission.Id, submission.SubmittedAt));
}
