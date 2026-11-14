namespace BandUp.Application.Features.Reading;

public sealed record PassageDto(
    Guid Id,
    string Title,
    string Body,
    int Version,
    DateTimeOffset? PublishedAt,
    IReadOnlyList<QuestionDto> Questions);
