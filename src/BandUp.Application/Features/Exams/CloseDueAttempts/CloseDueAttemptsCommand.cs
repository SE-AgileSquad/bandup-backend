using BandUp.Application.Abstractions;

namespace BandUp.Application.Features.Exams.CloseDueAttempts;

/// <summary>
/// Closes the signed-in learner's sittings whose deadline and grace have passed: the answers
/// already saved are graded, or the sitting is closed as expired when nothing was saved. With
/// <see cref="AttemptId"/> only that attempt is considered. The result is how many were closed.
/// The API runs it before it reads an attempt, its result or the learner's lists, so what is
/// read is never a finished sitting still shown as running.
/// </summary>
public sealed record CloseDueAttemptsCommand(Guid? AttemptId = null) : ICommand<int>;
