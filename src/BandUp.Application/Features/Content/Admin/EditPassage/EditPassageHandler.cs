using BandUp.Application.Abstractions;
using BandUp.Application.Common;
using BandUp.Domain.Content;

namespace BandUp.Application.Features.Content.Admin.EditPassage;

public sealed class EditPassageHandler(IAdminPassageStore store)
    : ICommandHandler<EditPassageCommand, bool>
{
    public async Task<Result<bool>> HandleAsync(EditPassageCommand command, CancellationToken cancellationToken)
    {
        Passage? passage = await store.LoadForUpdateAsync(command.PassageId, cancellationToken);
        if (passage is null)
        {
            return Result<bool>.Failure(Error.NotFound("Passage not found."));
        }

        if (passage.Status != PassageStatus.Draft)
        {
            return Result<bool>.Failure(Error.Conflict("Create a new revision to edit a published or archived passage."));
        }

        PassageInput input = command.Input;
        passage.EditDraft(input.Title, input.Body, input.SourceNote, input.UsageBasis);
        return Result<bool>.Success(true);
    }
}
