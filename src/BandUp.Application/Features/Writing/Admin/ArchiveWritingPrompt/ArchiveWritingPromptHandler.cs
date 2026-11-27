using BandUp.Application.Abstractions;
using BandUp.Application.Common;

namespace BandUp.Application.Features.Writing.Admin.ArchiveWritingPrompt;

public sealed class ArchiveWritingPromptHandler(IAdminWritingPromptStore store, TimeProvider clock)
    : ICommandHandler<ArchiveWritingPromptCommand, bool>
{
    public Task<Result<bool>> HandleAsync(ArchiveWritingPromptCommand command, CancellationToken cancellationToken) =>
        AdminWritingPromptChanges.ApplyAsync(store, command.PromptId,
            prompt => prompt.Archive(clock.GetUtcNow()), cancellationToken);
}

