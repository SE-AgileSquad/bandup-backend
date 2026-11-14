using BandUp.Application.Abstractions;

namespace BandUp.Application.Features.Content.Admin.SetQuestionAnswerKey;

public sealed record SetQuestionAnswerKeyCommand(Guid PassageId, Guid QuestionId, AnswerKeyInput Input) : ICommand<bool>;

public sealed record AnswerKeyInput(string CorrectAnswer, string[]? AcceptedVariants);
