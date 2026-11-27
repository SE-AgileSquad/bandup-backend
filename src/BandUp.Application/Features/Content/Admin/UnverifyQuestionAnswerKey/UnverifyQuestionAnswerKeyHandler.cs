using BandUp.Application.Abstractions;
using BandUp.Application.Common;

namespace BandUp.Application.Features.Content.Admin.UnverifyQuestionAnswerKey;

public sealed class UnverifyQuestionAnswerKeyHandler(IAdminPassageStore store)
    : ICommandHandler<UnverifyQuestionAnswerKeyCommand, bool>
{
    public Task<Result<bool>> HandleAsync(UnverifyQuestionAnswerKeyCommand command, CancellationToken cancellationToken) =>
        AdminDraftQuestion.ChangeAsync(store, command.PassageId, command.QuestionId, "answerKey.isVerified",
            question => question.ClearAnswerKeyVerification(), cancellationToken);
}
