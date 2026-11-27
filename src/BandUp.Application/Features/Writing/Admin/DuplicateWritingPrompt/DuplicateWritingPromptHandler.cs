using BandUp.Application.Abstractions;
using BandUp.Application.Common;
using BandUp.Domain.Writing;

namespace BandUp.Application.Features.Writing.Admin.DuplicateWritingPrompt;

public sealed class DuplicateWritingPromptHandler(IAdminWritingPromptStore store, ICurrentUser currentUser, TimeProvider clock)
    : ICommandHandler<DuplicateWritingPromptCommand, Guid>
{
    public async Task<Result<Guid>> HandleAsync(DuplicateWritingPromptCommand command, CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId is not { } actorId || actorId == Guid.Empty)
        {
            return Result<Guid>.Failure(Error.Unauthorized("An authenticated user is required."));
        }

        WritingPrompt? prompt = await store.LoadForUpdateAsync(command.PromptId, cancellationToken);
        if (prompt is null)
        {
            return Result<Guid>.Failure(Error.NotFound("Writing prompt not found."));
        }

        WritingPrompt copy = prompt.Duplicate(actorId, clock.GetUtcNow());
        await store.AddAsync(copy, cancellationToken);
        return Result<Guid>.Success(copy.Id);
    }
}

