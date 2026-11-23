using BandUp.Application.Abstractions;

namespace BandUp.Application.Features.Exams.StartAttempt;

public sealed record StartAttemptCommand(Guid PassageId) : ICommand<StartAttemptResult>;
