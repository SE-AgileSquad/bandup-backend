using BandUp.Application.Abstractions;
using BandUp.Application.Common;

namespace BandUp.Application.Features.Writing.Admin.PublishWritingPrompt;

public sealed class PublishWritingPromptHandler(IAdminWritingPromptStore store, TimeProvider clock)
    : ICommandHandler<PublishWritingPromptCommand, bool>
{
    public Task<Result<bool>> HandleAsync(PublishWritingPromptCommand command, CancellationToken cancellationToken) =>
        AdminWritingPromptChanges.ApplyAsync(store, command.PromptId,
            prompt => prompt.Publish(clock.GetUtcNow()), cancellationToken);
}

