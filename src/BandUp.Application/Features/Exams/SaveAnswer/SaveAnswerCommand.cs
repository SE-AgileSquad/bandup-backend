using BandUp.Application.Abstractions;

namespace BandUp.Application.Features.Exams.SaveAnswer;

/// <summary>
/// Autosave of one answer. A blank <see cref="SelectedAnswer"/> clears the saved answer.
/// </summary>
public sealed record SaveAnswerCommand(Guid AttemptId, Guid QuestionId, string? SelectedAnswer)
    : ICommand<AttemptStateDto>;
