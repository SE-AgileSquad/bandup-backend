using BandUp.Domain.Common;

namespace BandUp.Domain.Writing;

public enum WritingEvaluationStatus { Queued, Processing, Succeeded, Failed }

public sealed class WritingSubmission : Entity
{
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
        PromptSnapshot = new(prompt.Id, prompt.Title, prompt.TaskText);
        EssayText = essayText;
        SubmittedAt = submittedAt;
        QueueDeadline = queueDeadline;
    }

    public Guid UserId { get; }
    public Guid IdempotencyKey { get; }
    public WritingPromptSnapshot PromptSnapshot { get; }
    public string EssayText { get; }
    public DateTimeOffset SubmittedAt { get; }
    public DateTimeOffset QueueDeadline { get; }
    public DateTimeOffset? StartedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public WritingEvaluationStatus Status { get; private set; } = WritingEvaluationStatus.Queued;

    /// <summary>After lookup by owner and key, compare the stored payload before checking current prompt eligibility or credit.</summary>
    public bool MatchesPayload(Guid promptId, string essayText) =>
        promptId == PromptSnapshot.Id && string.Equals(EssayText, essayText, StringComparison.Ordinal);

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
