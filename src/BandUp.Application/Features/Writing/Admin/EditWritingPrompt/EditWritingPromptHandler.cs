using BandUp.Application.Abstractions;
using BandUp.Application.Common;

namespace BandUp.Application.Features.Writing.Admin.EditWritingPrompt;

public sealed class EditWritingPromptHandler(IAdminWritingPromptStore store, TimeProvider clock)
    : ICommandHandler<EditWritingPromptCommand, bool>
{
    public Task<Result<bool>> HandleAsync(EditWritingPromptCommand command, CancellationToken cancellationToken)
    {
        WritingPromptInput input = command.Input;
        return AdminWritingPromptChanges.ApplyAsync(store, command.PromptId,
            prompt => prompt.EditDraft(input.Title, input.TaskText, input.EssayType,
                input.MinWords, input.MaxWords, clock.GetUtcNow()), cancellationToken);
    }
}

