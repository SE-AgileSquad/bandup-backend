using BandUp.Application.Abstractions;

namespace BandUp.Application.Features.Content.Admin.UnverifyQuestionAnswerKey;

public sealed record UnverifyQuestionAnswerKeyCommand(Guid PassageId, Guid QuestionId) : ICommand<bool>;
