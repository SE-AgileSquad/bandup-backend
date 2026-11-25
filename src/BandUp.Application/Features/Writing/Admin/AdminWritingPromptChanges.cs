using BandUp.Application.Common;
using BandUp.Domain.Common;
using BandUp.Domain.Writing;

namespace BandUp.Application.Features.Writing.Admin;

internal static class AdminWritingPromptChanges
{
    internal static async Task<Result<bool>> ApplyAsync(IAdminWritingPromptStore store, Guid id,
        Action<WritingPrompt> change, CancellationToken cancellationToken)
    {
        WritingPrompt? prompt = await store.LoadForUpdateAsync(id, cancellationToken);
        if (prompt is null)
        {
            return Result<bool>.Failure(Error.NotFound("Writing prompt not found."));
        }

        try
        {
            change(prompt);
            return Result<bool>.Success(true);
        }
        catch (DomainException exception)
        {
            return Result<bool>.Failure(Error.Conflict(exception.Message));
        }
    }
}

