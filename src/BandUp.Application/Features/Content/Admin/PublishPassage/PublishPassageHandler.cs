using BandUp.Application.Abstractions;
using BandUp.Application.Common;
using BandUp.Domain.Common;
using BandUp.Domain.Content;

namespace BandUp.Application.Features.Content.Admin.PublishPassage;

public sealed class PublishPassageHandler(IAdminPassageStore store, TimeProvider clock)
    : ICommandHandler<PublishPassageCommand, bool>
{
    public async Task<Result<bool>> HandleAsync(PublishPassageCommand command, CancellationToken cancellationToken)
    {
        Passage? passage = await store.LoadForUpdateAsync(command.PassageId, cancellationToken);
        if (passage is null)
        {
            return Result<bool>.Failure(Error.NotFound("Passage not found."));
        }

        if (passage.Status == PassageStatus.Published)
        {
            return Result<bool>.Success(true);
        }

        try
        {
            passage.Publish(clock.GetUtcNow());
        }
        catch (DomainException exception)
        {
            return Result<bool>.Failure(Error.Conflict(exception.Message));
        }

        if (passage.PreviousVersionId is { } previousId)
        {
            Passage? previous = await store.LoadForUpdateAsync(previousId, cancellationToken);
            if (previous is { Status: PassageStatus.Published })
            {
                previous.Archive();
            }
        }

        return Result<bool>.Success(true);
    }
}
