using BandUp.Application.Abstractions;

namespace BandUp.Application.Features.Exams.GetPassageAttemptSummaries;

public sealed record GetPassageAttemptSummariesQuery : IQuery<IReadOnlyList<PassageAttemptSummaryDto>>;
