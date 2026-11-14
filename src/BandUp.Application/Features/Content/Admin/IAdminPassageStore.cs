using BandUp.Application.Common;
using BandUp.Domain.Content;

namespace BandUp.Application.Features.Content.Admin;

public interface IAdminPassageStore
{
    // Load the whole aggregate after acquiring the passage lock in the current command transaction.
    Task<Passage?> LoadForUpdateAsync(Guid id, CancellationToken cancellationToken);
    Task<bool> HasRevisionAsync(Guid id, CancellationToken cancellationToken);
    Task AddAsync(Passage passage, CancellationToken cancellationToken);
    Task<bool> TryDeletePassageAsync(Guid id, CancellationToken cancellationToken);
    Task<bool> TryDeleteQuestionAsync(Guid passageId, Guid questionId, CancellationToken cancellationToken);
    Task<PagedResult<PassageSummary>> ListAsync(PageRequest page, CancellationToken cancellationToken);
    Task<PassageDetails?> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<QuestionDetails?> GetQuestionAsync(Guid passageId, Guid questionId, CancellationToken cancellationToken);
}
