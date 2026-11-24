namespace BandUp.Application.Features.Reading;

/// <summary>
/// One row of the passage list: enough to choose a passage, never its questions.
/// </summary>
public sealed record PassageSummaryDto(
    Guid Id,
    string Title,
    int Version,
    DateTimeOffset? PublishedAt,
    int QuestionCount);
