using BandUp.Application.Common;
using BandUp.Domain.Writing;

namespace BandUp.Application.Features.Writing.Admin;

public interface IAdminWritingPromptStore
{
    Task AddAsync(WritingPrompt prompt, CancellationToken cancellationToken);
    Task<WritingPrompt?> LoadForUpdateAsync(Guid id, CancellationToken cancellationToken);
    Task<WritingPromptDetails?> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<PagedResult<WritingPromptSummary>> ListAsync(
        PageRequest page, WritingPromptStatus? status, string? search, CancellationToken cancellationToken);
    Task<bool> TryDeleteAsync(Guid id, CancellationToken cancellationToken);
}

