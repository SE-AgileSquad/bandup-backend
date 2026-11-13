using BandUp.Application.Abstractions;
using BandUp.Application.Common;

namespace BandUp.Application.Features.Content.Admin.SetQuestionAnswerKey;

public sealed class SetQuestionAnswerKeyHandler(IAdminPassageStore store)
    : ICommandHandler<SetQuestionAnswerKeyCommand, bool>
{
    public Task<Result<bool>> HandleAsync(SetQuestionAnswerKeyCommand command, CancellationToken cancellationToken) =>
        AdminDraftQuestion.ChangeAsync(store, command.PassageId, command.QuestionId, "answerKey",
            question => question.SetAnswerKey(command.Input.CorrectAnswer, command.Input.AcceptedVariants),
            cancellationToken);
}
