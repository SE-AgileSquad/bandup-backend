using BandUp.Application.Abstractions;
using BandUp.Domain.Exams;

namespace BandUp.Application.Features.Exams;

public interface IAttemptRepository : IRepository<Attempt>
{
    /// <summary>
    /// The attempt with its answers, or null. A plain read: it takes no lock.
    /// </summary>
    Task<Attempt?> GetWithAnswersAsync(Guid attemptId, CancellationToken cancellationToken);

    /// <summary>
    /// The attempt with its answers, locked until the surrounding transaction ends, so two
    /// concurrent changes to the same attempt run one after the other. Only meaningful inside
    /// a command, where the transaction behavior has already opened the transaction.
    /// </summary>
    Task<Attempt?> GetWithAnswersForUpdateAsync(Guid attemptId, CancellationToken cancellationToken);

    Task<Attempt?> FindInProgressAsync(Guid userId, Guid passageId, CancellationToken cancellationToken);

    /// <summary>
    /// Whether this learner already used the idempotency key to submit some attempt.
    /// </summary>
    Task<bool> IdempotencyKeyUsedAsync(Guid userId, string idempotencyKey, CancellationToken cancellationToken);

    /// <summary>
    /// Serializes concurrent starts by the same user on the same passage until the surrounding
    /// transaction ends, so the "is there already one?" check and the insert cannot interleave.
    /// </summary>
    Task AcquireStartLockAsync(Guid userId, Guid passageId, CancellationToken cancellationToken);
}
