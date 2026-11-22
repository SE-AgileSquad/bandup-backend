using BandUp.Domain.Content;

namespace BandUp.Application.Features.Content.Admin;

public sealed record PassageInput(string Title, string Body, string SourceNote, string UsageBasis);

public sealed record PassageSummary(
    Guid Id, string Title, PassageStatus Status, int Version, DateTimeOffset? PublishedAt,
    int QuestionCount, DateTimeOffset? UpdatedAt);

public sealed record PassageDetails(
    Guid Id,
    string Title,
    string Body,
    string SourceNote,
    string UsageBasis,
    PassageStatus Status,
    int Version,
    Guid? PreviousVersionId,
    Guid CreatedBy,
    DateTimeOffset? PublishedAt,
    IReadOnlyList<QuestionDetails> Questions,
    DateTimeOffset? UpdatedAt,
    bool CanCreateRevision,
    PassagePublicationReadiness PublishReadiness)
{
    public bool IsReadOnly => Status != PassageStatus.Draft;
    public int WordCount => Body.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
}

public sealed record QuestionDetails(
    Guid Id,
    Guid PassageId,
    QuestionType Type,
    string Prompt,
    int SortOrder,
    QuestionStatus Status,
    string? Explanation,
    string? EvidenceSentence,
    string? MisleadingSentence,
    string? UnspecifiedPart,
    AnswerKeyDetails? AnswerKey);

public sealed record AnswerKeyDetails(
    string CorrectAnswer, IReadOnlyList<string> AcceptedVariants, Guid? VerifiedBy, DateTimeOffset? VerifiedAt)
{
    public bool IsVerified => VerifiedBy.HasValue && VerifiedAt.HasValue;
}
