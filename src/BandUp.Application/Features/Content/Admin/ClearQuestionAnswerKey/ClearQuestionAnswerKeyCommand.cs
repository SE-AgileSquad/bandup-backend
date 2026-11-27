using BandUp.Application.Abstractions;

namespace BandUp.Application.Features.Content.Admin.ClearQuestionAnswerKey;

public sealed record ClearQuestionAnswerKeyCommand(Guid PassageId, Guid QuestionId) : ICommand<bool>;
