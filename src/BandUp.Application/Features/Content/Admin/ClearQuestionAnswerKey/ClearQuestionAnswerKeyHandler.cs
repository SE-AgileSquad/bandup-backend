using BandUp.Application.Abstractions;
using BandUp.Application.Common;

namespace BandUp.Application.Features.Content.Admin.ClearQuestionAnswerKey;

public sealed class ClearQuestionAnswerKeyHandler(IAdminPassageStore store)
    : ICommandHandler<ClearQuestionAnswerKeyCommand, bool>
{
    public Task<Result<bool>> HandleAsync(ClearQuestionAnswerKeyCommand command, CancellationToken cancellationToken) =>
        AdminDraftQuestion.ChangeAsync(store, command.PassageId, command.QuestionId, "answerKey",
            question => question.ClearAnswerKey(), cancellationToken);
}
