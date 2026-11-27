using BandUp.Application.Abstractions;
using BandUp.Application.Common;

namespace BandUp.Application.Features.Content.Admin.EditQuestion;

public sealed class EditQuestionHandler(IAdminPassageStore store, ICurrentUser currentUser, TimeProvider clock)
    : ICommandHandler<EditQuestionCommand, bool>
{
    public async Task<Result<bool>> HandleAsync(EditQuestionCommand command, CancellationToken cancellationToken)
    {
        var draft = await AdminDraftQuestion.LoadAsync(store, command.PassageId, command.QuestionId, cancellationToken);
        return draft.IsFailure ? Result<bool>.Failure(draft.Error!)
            : AdminQuestionContent.Apply(draft.Value, command.Input, currentUser, clock);
    }
}
