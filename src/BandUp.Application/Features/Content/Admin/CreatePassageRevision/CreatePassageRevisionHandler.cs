using BandUp.Application.Abstractions;
using BandUp.Application.Common;
using BandUp.Domain.Content;

namespace BandUp.Application.Features.Content.Admin.CreatePassageRevision;

public sealed class CreatePassageRevisionHandler(IAdminPassageStore store, ICurrentUser currentUser)
    : ICommandHandler<CreatePassageRevisionCommand, Guid>
{
    public async Task<Result<Guid>> HandleAsync(CreatePassageRevisionCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } actorId || actorId == Guid.Empty)
        {
            return Result<Guid>.Failure(Error.Unauthorized("An authenticated user is required."));
        }

        Passage? passage = await store.LoadForUpdateAsync(command.PassageId, cancellationToken);
        if (passage is null)
        {
            return Result<Guid>.Failure(Error.NotFound("Passage not found."));
        }

        if (passage.Status == PassageStatus.Draft || await store.HasRevisionAsync(passage.Id, cancellationToken))
        {
            return Result<Guid>.Failure(Error.Conflict("Only the latest previously published version can be revised."));
        }

        Passage revision = passage.CreateRevision(actorId);
        await store.AddAsync(revision, cancellationToken);
        return Result<Guid>.Success(revision.Id);
    }
}
