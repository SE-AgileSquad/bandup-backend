using BandUp.Application.Abstractions;
using BandUp.Application.Common;
using BandUp.Domain.Writing;

namespace BandUp.Application.Features.Writing.Admin.CreateWritingPrompt;

public sealed class CreateWritingPromptHandler(IAdminWritingPromptStore store, ICurrentUser currentUser, TimeProvider clock)
    : ICommandHandler<CreateWritingPromptCommand, Guid>
{
    public async Task<Result<Guid>> HandleAsync(CreateWritingPromptCommand command, CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId is not { } actorId || actorId == Guid.Empty)
        {
            return Result<Guid>.Failure(Error.Unauthorized("An authenticated user is required."));
        }

        WritingPromptInput input = command.Input;
        WritingPrompt prompt = WritingPrompt.CreateDraft(input.Title, input.TaskText, input.EssayType,
            input.MinWords, input.MaxWords, actorId, clock.GetUtcNow(), input.DurationMinutes ?? WritingPrompt.DefaultDurationMinutes);
        await store.AddAsync(prompt, cancellationToken);
        return Result<Guid>.Success(prompt.Id);
    }
}

