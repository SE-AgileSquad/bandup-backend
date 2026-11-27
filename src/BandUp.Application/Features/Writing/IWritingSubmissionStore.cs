using BandUp.Domain.Writing;

namespace BandUp.Application.Features.Writing;

/// <summary>Submission operations participating in the caller's unit of work.</summary>
public interface IWritingSubmissionStore
{
    /// <summary>After locking the owner's account, lock the prompt against Admin changes and evaluate its current eligibility.</summary>
    Task<WritingPrompt?> FindEligiblePromptAsync(Guid promptId, CancellationToken cancellationToken);
    Task<WritingSubmission?> FindByKeyAsync(Guid userId, Guid idempotencyKey, CancellationToken cancellationToken);

    /// <summary>Resolve immutable ownership without a row lock so settlement can lock the account first.</summary>
    Task<Guid?> FindOwnerAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Reload a tracked aggregate under a transaction-duration row lock. A claim takes only
    /// this lock. Any financial settlement must already hold the owner's credit account lock.</summary>
    Task<WritingSubmission?> LockSubmissionAsync(Guid id, CancellationToken cancellationToken);
    Task<WritingFeedback?> FindFeedbackAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Count outstanding submissions including pending writes while the owner account is locked.</summary>
    Task<int> CountOutstandingAsync(Guid owner, CancellationToken cancellationToken);
}

internal interface IWritingMutationWriter
{
    Task AddAcceptedSubmissionAsync(WritingSubmission submission, CancellationToken cancellationToken);
    void AuthorizeLifecycleChange(WritingSubmission submission);
    Task AddFeedbackAsync(WritingFeedback feedback, CancellationToken cancellationToken);
}
