using BandUp.Application.Abstractions;
using BandUp.Application.Common;

namespace BandUp.Application.Features.Content.Admin.EditQuestion;

public sealed class EditQuestionHandler(IAdminPassageStore store)
    : ICommandHandler<EditQuestionCommand, bool>
{
    public Task<Result<bool>> HandleAsync(EditQuestionCommand command, CancellationToken cancellationToken) =>
        AdminDraftQuestion.ChangeAsync(store, command.PassageId, command.QuestionId, "prompt",
            question => question.EditDraft(command.Input.Prompt, command.Input.SortOrder), cancellationToken);
}
