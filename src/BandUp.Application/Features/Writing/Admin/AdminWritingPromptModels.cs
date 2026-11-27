using BandUp.Domain.Writing;

namespace BandUp.Application.Features.Writing.Admin;

public sealed record WritingPromptInput(
    string Title, string TaskText, string EssayType = "Opinion essay",
    int MinWords = WritingPrompt.WritingMinWords, int MaxWords = WritingPrompt.WritingMaxWords);

public sealed record WritingPromptSummary(
    Guid Id, string Title, string EssayType, int MinWords, int MaxWords, WritingPromptStatus Status,
    DateTimeOffset UpdatedAt)
{
    public int TaskNumber => 2;
}

public sealed record WritingPromptDetails(
    Guid Id, string Title, string TaskText, string EssayType, int MinWords, int MaxWords,
    WritingPromptStatus Status, Guid CreatedBy, DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt, DateTimeOffset? PublishedAt)
{
    public int TaskNumber => 2;
    public bool IsReadOnly => Status != WritingPromptStatus.Draft;
}

