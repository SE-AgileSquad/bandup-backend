namespace BandUp.Application.Features.Exams;

public sealed record AttemptAnswerDto(Guid QuestionId, string SelectedAnswer);
