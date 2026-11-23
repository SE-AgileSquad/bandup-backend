using BandUp.Application.Abstractions;

namespace BandUp.Application.Features.Exams.GetAttemptResult;

public sealed record GetAttemptResultQuery(Guid AttemptId) : IQuery<AttemptResultDto>;
