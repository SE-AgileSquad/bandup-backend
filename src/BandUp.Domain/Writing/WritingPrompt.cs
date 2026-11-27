using BandUp.Domain.Common;

namespace BandUp.Domain.Writing;

public sealed class WritingPrompt : Entity
{
    public const int WritingMinWords = 100;
    public const int WritingMaxWords = 350;

    private WritingPrompt() { }

    public string Title { get; private set; } = null!;
    public string TaskText { get; private set; } = null!;
    public string EssayType { get; private set; } = null!;
    public int MinWords { get; private set; }
    public int MaxWords { get; private set; }
    public WritingPromptStatus Status { get; private set; } = WritingPromptStatus.Draft;
    public Guid CreatedBy { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset? PublishedAt { get; private set; }

    public static WritingPrompt CreateDraft(string title, string taskText, string essayType,
        int minWords, int maxWords, Guid createdBy, DateTimeOffset now)
    {
        if (createdBy == Guid.Empty)
        {
            throw new DomainException("A writing prompt requires a creator.");
        }

        WritingPrompt prompt = new() { CreatedBy = createdBy, CreatedAt = now, UpdatedAt = now };
        prompt.EditDraft(title, taskText, essayType, minWords, maxWords, now);
        return prompt;
    }

    public void EditDraft(string title, string taskText, string essayType,
        int minWords, int maxWords, DateTimeOffset now)
    {
        if (Status != WritingPromptStatus.Draft)
        {
            throw new DomainException("Only a draft writing prompt can be edited. Duplicate it to create a new draft.");
        }

        if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(taskText)
            || string.IsNullOrWhiteSpace(essayType) || title.Trim().Length > 320 || essayType.Trim().Length > 100)
        {
            throw new DomainException("A writing prompt requires a title, task text and essay type within their length limits.");
        }

        if (minWords <= 0 || maxWords < minWords)
        {
            throw new DomainException("The word range must be positive and the maximum must be at least the minimum.");
        }

        title = title.Trim();
        taskText = taskText.Trim();
        essayType = essayType.Trim();
        if (Title == title && TaskText == taskText && EssayType == essayType && MinWords == minWords && MaxWords == maxWords)
        {
            return;
        }

        Title = title;
        TaskText = taskText;
        EssayType = essayType;
        MinWords = minWords;
        MaxWords = maxWords;
        UpdatedAt = now;
    }

    public void Publish(DateTimeOffset now)
    {
        if (Status != WritingPromptStatus.Draft && Status != WritingPromptStatus.Published)
        {
            throw new DomainException("Only a draft writing prompt can be published.");
        }

        if (MinWords != WritingMinWords || MaxWords != WritingMaxWords)
        {
            throw new DomainException("Publishing a writing prompt requires minWords = 100 and maxWords = 350.");
        }

        if (Status == WritingPromptStatus.Published)
        {
            return;
        }

        // Publishing is the admin's content approval and makes the prompt eligible for learner use.
        Status = WritingPromptStatus.Published;
        PublishedAt = now;
        UpdatedAt = now;
    }

    public void Archive(DateTimeOffset now)
    {
        if (Status == WritingPromptStatus.Archived)
        {
            return;
        }

        if (Status != WritingPromptStatus.Published)
        {
            throw new DomainException("Only a published writing prompt can be archived.");
        }

        Status = WritingPromptStatus.Archived;
        UpdatedAt = now;
    }

    public WritingPrompt Duplicate(Guid createdBy, DateTimeOffset now) =>
        CreateDraft(Title, TaskText, EssayType, MinWords, MaxWords, createdBy, now);
}

