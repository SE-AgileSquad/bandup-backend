using BandUp.Domain.Content;

namespace BandUp.Application.Features.Content.Admin;

public sealed record PassageInput(string Title, string Body, string SourceNote, string UsageBasis);

public sealed record PassageSummary(Guid Id, string Title, PassageStatus Status, int Version, DateTimeOffset? PublishedAt);

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
    IReadOnlyList<QuestionDetails> Questions);

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
    string CorrectAnswer, IReadOnlyList<string> AcceptedVariants, Guid? VerifiedBy, DateTimeOffset? VerifiedAt);
