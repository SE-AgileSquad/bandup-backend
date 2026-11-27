using BandUp.Domain.Common;

namespace BandUp.Domain.Writing;

public enum WritingEvaluationStatus { Queued, Processing, Succeeded, Failed }

public sealed class WritingSubmission : Entity
{
    // Materialize accepted state from its own row, even after the catalog prompt is archived.
    private WritingSubmission() { }

    public WritingSubmission(Guid userId, Guid idempotencyKey, WritingPrompt prompt, string essayText,
        DateTimeOffset submittedAt, DateTimeOffset queueDeadline)
    {
        if (userId == Guid.Empty || idempotencyKey == Guid.Empty)
        {
            throw new DomainException("A submission requires a user and idempotency key.");
        }
        // New submissions cannot snapshot a prompt version that was not yet available at acceptance.
        if (!prompt.IsEligibleForSubmission() || submittedAt < prompt.UpdatedAt)
        {
            throw new DomainException("The prompt is unavailable.");
        }
        if (queueDeadline <= submittedAt)
        {
            throw new DomainException("Queue deadline must follow acceptance.");
        }
        if (!EssayPolicy.IsValid(essayText))
        {
            throw new DomainException("Essay content or length is invalid.");
        }
        UserId = userId;
        IdempotencyKey = idempotencyKey;
        PromptId = prompt.Id;
        PromptTitle = prompt.Title;
        PromptText = prompt.TaskText;
        EssayText = essayText;
        SubmittedAt = submittedAt;
        QueueDeadline = queueDeadline;
    }

    public Guid UserId { get; private set; }
    public Guid IdempotencyKey { get; private set; }
    public Guid PromptId { get; private set; }
    private string PromptTitle { get; set; } = null!;
    private string PromptText { get; set; } = null!;
    public WritingPromptSnapshot PromptSnapshot => new(PromptId, PromptTitle, PromptText);
    public string EssayText { get; private set; } = null!;
    public DateTimeOffset SubmittedAt { get; private set; }
    public DateTimeOffset QueueDeadline { get; private set; }

    // A queued row carries its retry schedule and lease identity across publisher restarts.
    public int PublishAttempts { get; private set; }
    public DateTimeOffset? NextPublishAttemptAt { get; private set; }
    public Guid? PublishLeaseId { get; private set; }
    public DateTimeOffset? PublishLeaseExpiresAt { get; private set; }
    public DateTimeOffset? PublishedAt { get; private set; }
    public DateTimeOffset? StartedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public WritingEvaluationStatus Status { get; private set; } = WritingEvaluationStatus.Queued;

    /// <summary>After lookup by owner and key, compare the stored payload before checking current prompt eligibility or credit.</summary>
    public bool MatchesPayload(Guid promptId, string essayText) =>
        promptId == PromptId && string.Equals(EssayText, essayText, StringComparison.Ordinal);

    public void StartProcessing(DateTimeOffset now)
    {
        if (Status != WritingEvaluationStatus.Queued || now < SubmittedAt || now >= QueueDeadline)
        {
            throw new DomainException("The submission cannot be claimed.");
        }
        Status = WritingEvaluationStatus.Processing;
        StartedAt = now;
    }

    public void Succeed(WritingFeedback feedback, DateTimeOffset now)
    {
        if (Status != WritingEvaluationStatus.Processing || feedback.Id != Id || now < StartedAt)
        {
            throw new DomainException("The submission cannot be completed with this feedback.");
        }
        Status = WritingEvaluationStatus.Succeeded;
        CompletedAt = now;
    }

    public void Fail(DateTimeOffset now)
    {
        bool eligible = Status switch
        {
            WritingEvaluationStatus.Queued => now >= QueueDeadline,
            WritingEvaluationStatus.Processing => now >= StartedAt,
            _ => false
        };
        if (!eligible)
        {
            throw new DomainException("The submission cannot be failed.");
        }
        Status = WritingEvaluationStatus.Failed;
        CompletedAt = now;
    }
}
