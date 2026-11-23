using BandUp.Application.Abstractions;
using BandUp.Application.Common;
using BandUp.Domain.Content;

namespace BandUp.Application.Features.Content.Admin.CreatePassage;

public sealed class CreatePassageHandler(IAdminPassageStore store, ICurrentUser currentUser)
    : ICommandHandler<CreatePassageCommand, Guid>
{
    public async Task<Result<Guid>> HandleAsync(CreatePassageCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } actorId || actorId == Guid.Empty)
        {
            return Result<Guid>.Failure(Error.Unauthorized("An authenticated user is required."));
        }

        PassageInput input = command.Input;
        Passage draft = Passage.CreateDraft(input.Title, input.Body, input.SourceNote, input.UsageBasis, actorId);
        await store.AddAsync(draft, cancellationToken);
        return Result<Guid>.Success(draft.Id);
    }
}
