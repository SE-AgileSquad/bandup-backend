using BandUp.Domain.Common;

namespace BandUp.Domain.Writing;

public sealed class WritingPrompt : Entity
{
    public WritingPrompt(Guid id, string title, string promptText, DateTimeOffset createdAt) : base(id)
    {
        if (id == Guid.Empty || string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(promptText))
        {
            throw new DomainException("A prompt requires an identity, title and text.");
        }
        Title = title;
        PromptText = promptText;
        CreatedAt = UpdatedAt = createdAt;
    }

    public string Title { get; }
    public string PromptText { get; }
    public bool IsApproved { get; private set; }
    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAt { get; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public void Approve(DateTimeOffset now)
    {
        UpdateTimestamp(now);
        IsApproved = true;
    }

    public void Activate(DateTimeOffset now)
    {
        if (!IsApproved)
        {
            throw new DomainException("Only approved prompts may be activated.");
        }
        UpdateTimestamp(now);
        IsActive = true;
    }

    public void Retire(DateTimeOffset now)
    {
        UpdateTimestamp(now);
        IsActive = false;
    }

    private void UpdateTimestamp(DateTimeOffset now)
    {
        if (now < UpdatedAt)
        {
            throw new DomainException("Prompt changes must be chronological.");
        }
        UpdatedAt = now;
    }
}

public sealed record WritingPromptSnapshot(Guid Id, string Title, string PromptText);
