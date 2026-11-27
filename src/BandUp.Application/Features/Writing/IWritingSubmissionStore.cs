using BandUp.Domain.Writing;

namespace BandUp.Application.Features.Writing;

/// <summary>Acceptance operations participating in the caller's unit of work.</summary>
public interface IWritingSubmissionStore
{
    /// <summary>After locking the owner's account, lock the prompt against Admin changes and evaluate its current eligibility.</summary>
    Task<WritingPrompt?> FindEligiblePromptAsync(Guid promptId, CancellationToken cancellationToken);
    Task<WritingSubmission?> FindByKeyAsync(Guid userId, Guid idempotencyKey, CancellationToken cancellationToken);
    Task AddAsync(WritingSubmission submission, CancellationToken cancellationToken);
}
