using BandUp.Application.Abstractions;

namespace BandUp.Application.Features.Content.Admin.DeleteQuestion;

public sealed record DeleteQuestionCommand(Guid PassageId, Guid QuestionId) : ICommand<bool>;
