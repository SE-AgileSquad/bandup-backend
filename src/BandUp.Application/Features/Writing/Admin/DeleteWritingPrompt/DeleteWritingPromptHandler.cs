using BandUp.Application.Abstractions;
using BandUp.Application.Common;
using BandUp.Domain.Writing;

namespace BandUp.Application.Features.Writing.Admin.DeleteWritingPrompt;

public sealed class DeleteWritingPromptHandler(IAdminWritingPromptStore store, TimeProvider clock)
    : ICommandHandler<DeleteWritingPromptCommand, bool>
{
    public async Task<Result<bool>> HandleAsync(DeleteWritingPromptCommand command, CancellationToken cancellationToken)
    {
        WritingPrompt? prompt = await store.LoadForUpdateAsync(command.PromptId, cancellationToken);
        if (prompt is null)
        {
            return Result<bool>.Failure(Error.NotFound("Writing prompt not found."));
        }

        if (prompt.Status == WritingPromptStatus.Draft)
        {
            return await store.TryDeleteAsync(prompt.Id, cancellationToken)
                ? Result<bool>.Success(true)
                : Result<bool>.Failure(Error.Conflict("The writing prompt is referenced by other records."));
        }

        prompt.Archive(clock.GetUtcNow());
        return Result<bool>.Success(true);
    }
}

