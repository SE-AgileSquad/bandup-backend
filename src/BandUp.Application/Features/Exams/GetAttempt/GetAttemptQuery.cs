using BandUp.Application.Abstractions;

namespace BandUp.Application.Features.Exams.GetAttempt;

public sealed record GetAttemptQuery(Guid AttemptId) : IQuery<AttemptStateDto>;
