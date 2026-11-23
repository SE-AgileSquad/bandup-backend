using BandUp.Application.Abstractions;
using BandUp.Application.Common;
using BandUp.Domain.Content;

namespace BandUp.Application.Features.Content.Admin.DeletePassage;

public sealed class DeletePassageHandler(IAdminPassageStore store)
    : ICommandHandler<DeletePassageCommand, bool>
{
    public async Task<Result<bool>> HandleAsync(DeletePassageCommand command, CancellationToken cancellationToken)
    {
        Passage? passage = await store.LoadForUpdateAsync(command.PassageId, cancellationToken);
        if (passage is null)
        {
            return Result<bool>.Failure(Error.NotFound("Passage not found."));
        }

        if (passage.Status == PassageStatus.Draft)
        {
            return await store.TryDeletePassageAsync(passage.Id, cancellationToken)
                ? Result<bool>.Success(true)
                : Result<bool>.Failure(Error.Conflict("The passage or its questions are referenced by other records."));
        }

        if (passage.Status == PassageStatus.Published)
        {
            passage.Archive();
        }

        return Result<bool>.Success(true);
    }
}
