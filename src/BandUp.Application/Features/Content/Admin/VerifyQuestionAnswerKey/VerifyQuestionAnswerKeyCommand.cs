using BandUp.Application.Abstractions;

namespace BandUp.Application.Features.Content.Admin.VerifyQuestionAnswerKey;

public sealed record VerifyQuestionAnswerKeyCommand(Guid PassageId, Guid QuestionId) : ICommand<bool>;
